using System.Collections.Immutable;
using Axorith.Core.Logging;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
/// The concrete implementation of the module registry.
/// It loads all modules asynchronously after being constructed.
/// </summary>
public class ModuleRegistry : IModuleRegistry
{
    private IReadOnlyDictionary<Guid, Type> _moduleTypes = ImmutableDictionary<Guid, Type>.Empty;
    private IReadOnlyList<IModule> _moduleDefinitions = ImmutableList<IModule>.Empty;
    private bool _isInitialized;
    
    private readonly IModuleLoader _moduleLoader;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModuleRegistry> _logger;
    
    public ModuleRegistry(IModuleLoader moduleLoader, IServiceProvider serviceProvider, ILogger<ModuleRegistry> logger)
    {
        _moduleLoader = moduleLoader;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Asynchronously loads all modules from the default search paths.
    /// This must be called before the registry can be used.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing module registry...");
        
        var (definitions, types) = await _moduleLoader.LoadModuleTypesAsync(GetDefaultSearchPaths(), cancellationToken);
        _moduleDefinitions = definitions;
        _moduleTypes = types;
        _isInitialized = true;

        _logger.LogInformation("Module registry initialized with {Count} modules.", _moduleTypes.Count);
    }

    public IReadOnlyList<IModule> GetAllDefinitions()
    {
        EnsureInitialized();
        return _moduleDefinitions;
    }

    public IModule? GetDefinitionById(Guid moduleId)
    {
        EnsureInitialized();
        return _moduleDefinitions.FirstOrDefault(m => m.Id == moduleId);
    }

    public IModule? CreateInstance(Guid moduleId)
    {
        EnsureInitialized();
        if (_moduleTypes.TryGetValue(moduleId, out var moduleType))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var scopeProvider = scope.ServiceProvider;
                var loggerFactory = scopeProvider.GetRequiredService<ILoggerFactory>();
                var genericLogger = loggerFactory.CreateLogger(moduleType);
                var moduleLogger = new ModuleLoggerAdapter(genericLogger);
                
                var services = new object[] { moduleLogger };
                return ActivatorUtilities.CreateInstance(scopeProvider, moduleType, services) as IModule;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create an instance of module with ID {ModuleId}", moduleId);
                return null;
            }
        }
        return null;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("ModuleRegistry has not been initialized. Call InitializeAsync() first.");
        }
    }

    private static IEnumerable<string> GetDefaultSearchPaths()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Axorith", "modules");
        var devPath = Path.Combine(AppContext.BaseDirectory, "modules");
        return new[] { appDataPath, devPath };
    }
}