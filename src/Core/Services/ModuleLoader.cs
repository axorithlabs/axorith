using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Microsoft.Extensions.Logging;
using static Axorith.Shared.Utils.EnvironmentUtils;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation for discovering and loading module definitions.
/// </summary>
public class ModuleLoader(ILogger<ModuleLoader> logger) : IModuleLoader
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <inheritdoc />
    public Task<IReadOnlyList<ModuleDefinition>> LoadModuleDefinitionsAsync(IEnumerable<string> searchPaths,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting module definition discovery from 'module.json' files...");
        var definitions = new List<ModuleDefinition>();
        var currentPlatform = GetCurrentPlatform();

        foreach (var path in searchPaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.LinkTarget != null)
            {
                var sanitizedPath = path.Replace('\n', '_').Replace('\r', '_');
                logger.LogWarning("Module directory '{ModuleDir}' is a symbolic link. Skipping for security reasons.",
                    path);
                continue;
            }

            if (!Directory.Exists(path))
            {
                var sanitizedPath = path.Replace('\n', '_').Replace('\r', '_');
                logger.LogWarning("Module search path not found, skipping: {Path}", sanitizedPath);
                continue;
            }

            logger.LogDebug("Scanning directory for modules: {Path}", path);

            var moduleDirectories = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly);
            foreach (var moduleDir in moduleDirectories)
            {
                var jsonFile = Path.Combine(moduleDir, "module.json");

                if (!File.Exists(jsonFile)) continue;

                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var jsonString = File.ReadAllText(jsonFile);

                    if (new FileInfo(jsonFile).Length > 10 * 1024)
                    {
                        logger.LogWarning("module.json file is too large: {Path}. Skipping.", jsonFile);
                        continue;
                    }

                    var definition = JsonSerializer.Deserialize<ModuleDefinition>(jsonString, _jsonOptions);

                    if (definition == null)
                    {
                        logger.LogWarning(
                            "Failed to deserialize module.json at {JsonPath}, it resulted in a null object.",
                            jsonFile);
                        continue;
                    }

                    if (!definition.Platforms.Contains(currentPlatform))
                    {
                        logger.LogDebug(
                            "Skipping module '{ModuleName}' as it does not support the current platform ({Platform}).",
                            definition.Name, currentPlatform);
                        continue;
                    }

                    var moduleDirectory = Path.GetDirectoryName(jsonFile);
                    if (moduleDirectory == null) continue;

                    var dllFile = Directory.EnumerateFiles(moduleDirectory, "*.dll").FirstOrDefault();
                    if (dllFile == null)
                    {
                        logger.LogWarning(
                            "No DLL found in the directory of {JsonFile}. Skipping module '{ModuleName}'.",
                            jsonFile, definition.Name);
                        continue;
                    }
                    
                    var loadContext = new ModuleAssemblyLoadContext(dllFile);
                    var assembly = loadContext.LoadFromAssemblyPath(dllFile);
                    var moduleType = assembly.GetExportedTypes()
                        .FirstOrDefault(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    if (moduleType != null)
                    {
                        definition.LoadContext = loadContext;
                        definition.ModuleType = moduleType;
                        
                        definitions.Add(definition);
                        
                        logger.LogInformation("Discovered module definition '{ModuleName}' from {DllFile}",
                            definition.Name, Path.GetFileName(dllFile));
                    }
                    else
                    {
                        logger.LogWarning(
                            "DLL {DllFile} does not contain a public class implementing IModule. Skipping module '{ModuleName}'.",
                            Path.GetFileName(dllFile), definition.Name);
                        
                        loadContext.Unload();
                    }
                }
                catch (JsonException jsonEx)
                {
                    logger.LogError(jsonEx, "Invalid JSON format in {JsonFile}. Skipping.", jsonFile);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load module definition from {JsonFile}", jsonFile);
                }
            }
        }

        logger.LogInformation("Module discovery finished. Found {Count} compatible module definitions.",
            definitions.Count);
        return Task.FromResult<IReadOnlyList<ModuleDefinition>>(definitions);
    }
}