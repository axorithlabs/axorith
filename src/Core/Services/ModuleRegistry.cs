using System.Collections.Immutable;
using Autofac;
using Axorith.Core.Logging;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation of the module registry.
///     It uses Autofac to create isolated lifetime scopes for each module instance.
/// </summary>
public class ModuleRegistry(ILifetimeScope rootScope, IModuleLoader moduleLoader, ILogger<ModuleRegistry> logger)
    : IModuleRegistry, IDisposable
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
        logger.LogInformation("Initializing module registry...");
        var definitionsList =
            await moduleLoader.LoadModuleDefinitionsAsync(GetDefaultSearchPaths(), cancellationToken);
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
        foreach (var definition in _definitions.Values)
            try
            {
                if (definition.LoadContext?.IsCollectible != true) continue;

                definition.LoadContext.Unload();
                logger.LogDebug("Unloaded assembly context for module '{ModuleName}'", definition.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to unload assembly context for module '{ModuleName}'", definition.Name);
            }

        _definitions = ImmutableDictionary<Guid, ModuleDefinition>.Empty;
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
                    // Register module-specific services.
                    // Each module instance gets its own logger and http client.
                    builder.RegisterInstance(definition).As<ModuleDefinition>();

                    builder.Register(c =>
                            new ModuleLoggerAdapter(c.Resolve<ILoggerFactory>().CreateLogger(definition.ModuleType)))
                        .As<IModuleLogger>()
                        .InstancePerLifetimeScope();
                    
                    builder.Register(_ =>
                        {
                            var underlyingStorage = rootScope.Resolve<ISecureStorageService>();
                            
                            return new ModuleScopedSecureStorage(underlyingStorage, definition);
                        })
                        .As<ISecureStorageService>()
                        .InstancePerLifetimeScope();

                    // Register the module type itself.
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

    private static IEnumerable<string> GetDefaultSearchPaths()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Axorith",
            "modules");
        var devPath = Path.Combine(AppContext.BaseDirectory, "modules");
        return new[] { appDataPath, devPath };
    }
}
