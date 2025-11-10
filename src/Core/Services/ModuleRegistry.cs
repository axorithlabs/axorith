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
    private readonly IEnumerable<string> _allowedSymlinks;
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
    /// <param name="allowedSymlinks">Whitelist of allowed symlink paths.</param>
    /// <param name="logger">The logger instance.</param>
    public ModuleRegistry(
        ILifetimeScope rootScope,
        IModuleLoader moduleLoader,
        IEnumerable<string> searchPaths,
        IEnumerable<string> allowedSymlinks,
        ILogger<ModuleRegistry> logger)
    {
        _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
        _moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
        _searchPaths = searchPaths ?? throw new ArgumentNullException(nameof(searchPaths));
        _allowedSymlinks = allowedSymlinks ?? throw new ArgumentNullException(nameof(allowedSymlinks));
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
            await _moduleLoader.LoadModuleDefinitionsAsync(_searchPaths, cancellationToken, _allowedSymlinks)
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
        
        var unloadedContexts = new List<(string Name, WeakReference WeakRef)>();
        
        foreach (var definition in _definitions.Values)
            try
            {
                if (definition.LoadContext?.IsCollectible != true) continue;

                var weakRef = new WeakReference(definition.LoadContext);
                definition.LoadContext.Unload();
                unloadedContexts.Add((definition.Name, weakRef));
                _logger.LogDebug("Unloaded assembly context for module '{ModuleName}'", definition.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload assembly context for module '{ModuleName}'", definition.Name);
            }

        _definitions = ImmutableDictionary<Guid, ModuleDefinition>.Empty;
        
        // Force garbage collection to ensure finalizers run and native resources are released
        if (unloadedContexts.Count > 0)
        {
            _logger.LogDebug("Running GC to finalize {Count} unloaded contexts", unloadedContexts.Count);
            
            for (int i = 0; i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                // Check if all contexts are collected
                var stillAlive = unloadedContexts.Count(c => c.WeakRef.IsAlive);
                if (stillAlive == 0)
                {
                    _logger.LogDebug("All assembly contexts collected after {Iterations} GC iterations", i + 1);
                    break;
                }
                
                if (i == 9 && stillAlive > 0)
                {
                    _logger.LogWarning("{Count} assembly contexts still alive after 10 GC iterations - potential memory leak", stillAlive);
                    foreach (var context in unloadedContexts.Where(c => c.WeakRef.IsAlive))
                    {
                        _logger.LogWarning("AssemblyLoadContext '{Name}' not collected", context.Name);
                    }
                }
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