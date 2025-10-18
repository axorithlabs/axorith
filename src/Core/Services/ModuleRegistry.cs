using System.Collections.Immutable;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
/// The concrete implementation of the module registry.
/// It loads all modules asynchronously after being constructed.
/// </summary>
public class ModuleRegistry(IModuleLoader moduleLoader, ILogger<ModuleRegistry> logger) : IModuleRegistry
{
    private IReadOnlyDictionary<Guid, IModule> _modules = ImmutableDictionary<Guid, IModule>.Empty;
    private IReadOnlyList<IModule> _moduleList = ImmutableList<IModule>.Empty;
    private bool _isInitialized;

    /// <summary>
    /// Asynchronously loads all modules from the default search paths.
    /// This must be called before the registry can be used.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing module registry...");
        
        _moduleList = await moduleLoader.LoadModulesAsync(GetDefaultSearchPaths(), cancellationToken);
        _modules = _moduleList.ToDictionary(m => m.Id);
        _isInitialized = true;

        logger.LogInformation("Module registry initialized with {Count} modules.", _modules.Count);
    }

    public IReadOnlyList<IModule> GetAllModules()
    {
        EnsureInitialized();
        return _moduleList;
    }

    public IModule? GetModuleById(Guid moduleId)
    {
        EnsureInitialized();
        return _modules.GetValueOrDefault(moduleId);
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