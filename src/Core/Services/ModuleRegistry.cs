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
    private IReadOnlyDictionary<Guid, Type> _moduleTypes = ImmutableDictionary<Guid, Type>.Empty;
    private IReadOnlyList<IModule> _moduleDefs = ImmutableList<IModule>.Empty;
    private bool _isInitialized;

    /// <summary>
    /// Asynchronously loads all modules from the default search paths.
    /// This must be called before the registry can be used.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing module registry...");
        
        var (definitions, types) = await moduleLoader.LoadModuleTypesAsync(GetDefaultSearchPaths(), cancellationToken);
        _moduleDefs = definitions;
        _moduleTypes = types;
        _isInitialized = true;

        logger.LogInformation("Module registry initialized with {Count} modules.", _moduleTypes.Count);
    }

    public IReadOnlyList<IModule> GetAllModuleDefs()
    {
        EnsureInitialized();
        return _moduleDefs;
    }

    public IModule? GetModuleDefById(Guid moduleId)
    {
        EnsureInitialized();
        return _moduleDefs.FirstOrDefault(m => m.Id == moduleId);
    }

    public IModule? CreateModuleInstance(Guid moduleId)
    {
        EnsureInitialized();
        if (_moduleTypes.TryGetValue(moduleId, out var moduleType))
        {
            try
            {
                return Activator.CreateInstance(moduleType) as IModule;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create an instance of module with ID {ModuleId}", moduleId);
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