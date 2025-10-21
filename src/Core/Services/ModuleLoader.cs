using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Microsoft.Extensions.Logging;
using System.Runtime.Loader;
using static Axorith.Shared.Utils.EnvironmentUtils;

namespace Axorith.Core.Services;

/// <summary>
/// The concrete implementation for loading modules from the file system.
/// </summary>
public class ModuleLoader(ILogger<ModuleLoader> logger) : IModuleLoader
{
    public Task<(IReadOnlyList<IModule> Definitions, IReadOnlyDictionary<Guid, Type> Types)> LoadModuleTypesAsync(IEnumerable<string> searchPaths, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting module discovery...");

        var definitions = new List<IModule>();
        var types = new Dictionary<Guid, Type>();
        var currentPlatform = GetCurrentPlatform();

        foreach (var path in searchPaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                logger.LogWarning("Module search path not found: {Path}", path);
                continue;
            }

            logger.LogDebug("Scanning directory: {Path}", directory.FullName);

            var dllFiles = directory.EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly);
            foreach (var file in dllFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                try
                {
                    // We use AssemblyLoadContext.Default here for simplicity. For a more advanced
                    // plugin system that supports dynamic loading, unloading, and versioning of modules,
                    // a custom AssemblyLoadContext would be required for each module to isolate its dependencies.
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file.FullName);

                    var moduleType = assembly.GetExportedTypes()
                        .FirstOrDefault(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    if (moduleType != null)
                    {
                        if (Activator.CreateInstance(moduleType) is IModule moduleInstance)
                        {
                            if (moduleInstance.SupportedPlatforms.Contains(currentPlatform))
                            {
                                logger.LogInformation("Successfully loaded module '{ModuleName}' from {Assembly}", moduleInstance.Name, file.Name);
                                definitions.Add(moduleInstance); 
                                types[moduleInstance.Id] = moduleType;
                            }
                            else
                            {
                                logger.LogDebug("Skipping module '{ModuleName}' as it does not support the current platform ({Platform})", moduleInstance.Name, currentPlatform);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load assembly {FileName}", file.Name);
                }
            }
        }

        logger.LogInformation("Module discovery finished. Found {Count} compatible modules.", definitions.Count);
        return Task.FromResult<(IReadOnlyList<IModule>, IReadOnlyDictionary<Guid, Type>)>((definitions, types));
    }
}