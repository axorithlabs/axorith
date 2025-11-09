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
public class ModuleRegistry : IModuleRegistry, IDisposable
{
    private readonly ILifetimeScope _rootScope;
    private readonly IModuleLoader _moduleLoader;
    private readonly IEnumerable<string> _searchPaths;
    private readonly ILogger<ModuleRegistry> _logger;

    private IReadOnlyDictionary<Guid, ModuleDefinition>
        _definitions = ImmutableDictionary<Guid, ModuleDefinition>.Empty;

    private bool _isInitialized;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleRegistry"/> class.
    /// </summary>
    /// <param name="rootScope">The root Autofac lifetime scope.</param>
    /// <param name="moduleLoader">The module loader for discovering module assemblies.</param>
    /// <param name="searchPaths">The resolved search paths from configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public ModuleRegistry(
        ILifetimeScope rootScope,
        IModuleLoader moduleLoader,
        IEnumerable<string> searchPaths,
        ILogger<ModuleRegistry> logger)
    {
        _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
        _moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
        _searchPaths = searchPaths ?? throw new ArgumentNullException(nameof(searchPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Asynchronously loads all module definitions using the module loader.
    ///     This must be called before any other methods on this class.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing module registry with search paths: {SearchPaths}",
            string.Join(", ", _searchPaths));

        var definitionsList =
            await _moduleLoader.LoadModuleDefinitionsAsync(_searchPaths, cancellationToken)
                .ConfigureAwait(false);

        _definitions = definitionsList.ToDictionary(d => d.Id);
        _isInitialized = true;

        _logger.LogInformation("Module registry initialized with {Count} modules", _definitions.Count);
    }

    /// <summary>
    ///     Unloads all loaded module assemblies from memory.
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("Disposing ModuleRegistry and unloading all module contexts...");
        foreach (var definition in _definitions.Values)
            try
            {
                if (definition.LoadContext?.IsCollectible != true) continue;

                definition.LoadContext.Unload();
                _logger.LogDebug("Unloaded assembly context for module '{ModuleName}'", definition.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload assembly context for module '{ModuleName}'", definition.Name);
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
                var moduleScope = _rootScope.BeginLifetimeScope(builder =>
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
                            var underlyingStorage = _rootScope.Resolve<ISecureStorageService>();

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
                _logger.LogError(ex, "Failed to create module instance for {ModuleName} due to an exception",
                    definition.Name);
                return (null, null);
            }

        _logger.LogWarning(
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