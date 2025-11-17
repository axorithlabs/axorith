using System.Collections.Immutable;
using Autofac;
using Axorith.Core.Logging;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation of the module registry.
///     It uses Autofac to create isolated lifetime scopes for each module instance.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="ModuleRegistry" /> class.
/// </remarks>
/// <param name="rootScope">The root Autofac lifetime scope.</param>
/// <param name="moduleLoader">The module loader for discovering module assemblies.</param>
/// <param name="searchPaths">The resolved search paths from configuration.</param>
/// <param name="allowedSymlinks">Whitelist of allowed symlink paths.</param>
/// <param name="logger">The logger instance.</param>
public class ModuleRegistry(
    ILifetimeScope rootScope,
    IModuleLoader moduleLoader,
    IEnumerable<string> searchPaths,
    IEnumerable<string> allowedSymlinks,
    ILogger<ModuleRegistry> logger,
    bool enableAggressiveUnloadGc = false) : IModuleRegistry, IDisposable
{
    private IReadOnlyDictionary<Guid, ModuleDefinition>
        _definitions = ImmutableDictionary<Guid, ModuleDefinition>.Empty;

    private bool _isInitialized;

    /// <summary>
    ///     Asynchronously loads all module definitions using the module loader.
    ///     This must be called before any other methods on this class.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing module registry with search paths: {SearchPaths}",
            string.Join(", ", searchPaths));

        var definitionsList =
            await moduleLoader.LoadModuleDefinitionsAsync(searchPaths, cancellationToken, allowedSymlinks)
                .ConfigureAwait(false);

        _definitions = definitionsList.ToDictionary(d => d.Id);
        _isInitialized = true;

        logger.LogInformation("Module registry initialized with {Count} modules", _definitions.Count);
    }

    /// <summary>
    ///     Unloads all loaded module assemblies from memory.
    /// </summary>
    public void Dispose()
    {
        logger.LogInformation("Disposing ModuleRegistry and unloading all module contexts...");

        var unloadedContexts = new List<(string Name, WeakReference WeakRef)>();

        foreach (var definition in _definitions.Values)
            try
            {
                if (definition.LoadContext?.IsCollectible != true) continue;

                var weakRef = new WeakReference(definition.LoadContext);
                definition.LoadContext.Unload();
                unloadedContexts.Add((definition.Name, weakRef));
                logger.LogDebug("Unloaded assembly context for module '{ModuleName}'", definition.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to unload assembly context for module '{ModuleName}'", definition.Name);
            }

        _definitions = ImmutableDictionary<Guid, ModuleDefinition>.Empty;

        if (unloadedContexts.Count <= 0) return;

        if (!enableAggressiveUnloadGc)
        {
            logger.LogDebug("Skipping aggressive GC for {Count} unloaded module contexts", unloadedContexts.Count);
            return;
        }

        logger.LogDebug("Running GC to finalize {Count} unloaded contexts", unloadedContexts.Count);

        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var stillAlive = unloadedContexts.Count(c => c.WeakRef.IsAlive);
            if (stillAlive == 0)
            {
                logger.LogDebug("All assembly contexts collected after {Iterations} GC iterations", i + 1);
                break;
            }

            if (i == 9 && stillAlive > 0)
            {
                logger.LogWarning(
                    "{Count} assembly contexts still alive after 10 GC iterations - potential memory leak",
                    stillAlive);
                foreach (var (name, _) in unloadedContexts.Where(c => c.WeakRef.IsAlive))
                    logger.LogWarning("AssemblyLoadContext '{Name}' not collected", name);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleDefinition> GetAllDefinitions()
    {
        EnsureInitialized();
        return _definitions.Values.ToList();
    }

    /// <inheritdoc />
    public ModuleDefinition? GetDefinitionById(Guid moduleId)
    {
        EnsureInitialized();
        return _definitions.GetValueOrDefault(moduleId);
    }

    /// <inheritdoc />
    public (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid moduleId)
    {
        EnsureInitialized();
        if (_definitions.TryGetValue(moduleId, out var definition) && definition.ModuleType != null)
            try
            {
                var moduleScope = rootScope.BeginLifetimeScope(builder =>
                {
                    builder.RegisterInstance(definition).As<ModuleDefinition>();

                    builder.Register(c =>
                        {
                            var loggerFactory = c.Resolve<ILoggerFactory>();
                            var innerLogger = loggerFactory.CreateLogger(definition.ModuleType);
                            return new ModuleLoggerAdapter(innerLogger, definition.Name);
                        })
                        .As<IModuleLogger>()
                        .InstancePerLifetimeScope();

                    builder.Register(_ =>
                        {
                            var underlyingStorage = rootScope.Resolve<ISecureStorageService>();

                            return new ModuleScopedSecureStorage(underlyingStorage, definition);
                        })
                        .As<ISecureStorageService>()
                        .InstancePerLifetimeScope();

                    builder.RegisterType(definition.ModuleType).As<IModule>();
                });

                var instance = moduleScope.Resolve<IModule>();

                return (instance, moduleScope);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create module instance for {ModuleName} due to an exception",
                    definition.Name);
                return (null, null);
            }

        logger.LogWarning(
            "Could not create instance for module ID {ModuleId}. Definition not found or module type is null",
            moduleId);
        return (null, null);
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "ModuleRegistry has not been initialized. Call InitializeAsync() first.");
    }
}