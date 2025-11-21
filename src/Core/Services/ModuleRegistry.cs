using System.Collections.Immutable;
using System.Runtime.Loader;
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
public class ModuleRegistry(
    ILifetimeScope rootScope,
    IModuleLoader moduleLoader,
    IEnumerable<string> searchPaths,
    IEnumerable<string> allowedSymlinks,
    ILogger<ModuleRegistry> logger) : IModuleRegistry, IDisposable
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
    ///     This operation is non-blocking. Actual memory release happens in the background.
    /// </summary>
    public void Dispose()
    {
        _isInitialized = false;

        logger.LogInformation("Disposing ModuleRegistry and initiating module unload...");

        var contextsToTrack = new List<(string Name, WeakReference<AssemblyLoadContext> Ref)>();

        foreach (var definition in _definitions.Values)
        {
            if (definition.LoadContext?.IsCollectible != true)
            {
                continue;
            }

            try
            {
                contextsToTrack.Add((definition.Name, new WeakReference<AssemblyLoadContext>(definition.LoadContext)));

                definition.LoadContext.Unload();
                logger.LogDebug("Initiated unload for module '{ModuleName}'", definition.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initiate unload for module '{ModuleName}'", definition.Name);
            }
            finally
            {
                definition.LoadContext = null;
                definition.ModuleType = null;
            }
        }

        _definitions = ImmutableDictionary<Guid, ModuleDefinition>.Empty;

        if (contextsToTrack.Count > 0)
        {
            // Fire-and-forget monitoring task. Does not block Dispose.
            _ = Task.Run(() => MonitorUnloadingAsync(contextsToTrack));
        }

        GC.SuppressFinalize(this);
    }

    private async Task MonitorUnloadingAsync(List<(string Name, WeakReference<AssemblyLoadContext> Ref)> contexts)
    {
        const int maxChecks = 10;
        const int delayMs = 500;

        for (var i = 0; i < maxChecks; i++)
        {
            await Task.Delay(delayMs);

            contexts.RemoveAll(c => !c.Ref.TryGetTarget(out _));

            if (contexts.Count != 0)
            {
                continue;
            }

            logger.LogDebug("All module contexts unloaded successfully.");
            return;
        }

        // If we are here, some contexts are still alive after ~5 seconds.
        // This indicates a likely memory leak (strong reference held somewhere), 
        // but we do NOT force GC to fix it. We just report it.
        var leaks = string.Join(", ", contexts.Select(c => c.Name));
        logger.LogWarning(
            "The following modules have not unloaded after {Timeout}s: {Modules}. " +
            "This may indicate a memory leak (e.g. static event handlers, running threads). " +
            "They will be collected by GC eventually if no strong references remain.",
            (maxChecks * delayMs) / 1000.0, leaks);
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
        {
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
        }

        logger.LogWarning(
            "Could not create instance for module ID {ModuleId}. Definition not found or module type is null",
            moduleId);
        return (null, null);
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                "ModuleRegistry has not been initialized. Call InitializeAsync() first.");
        }
    }
}