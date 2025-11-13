using System.Diagnostics;
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
    public async Task<IReadOnlyList<ModuleDefinition>> LoadModuleDefinitionsAsync(
        IEnumerable<string> searchPaths,
        CancellationToken cancellationToken,
        IEnumerable<string>? allowedSymlinks = null)
    {
        logger.LogInformation("Starting module definition discovery from 'module.json' files...");
        var definitions = new List<ModuleDefinition>();
        var currentPlatform = GetCurrentPlatform();
        var allowedSymlinkSet = allowedSymlinks?.Select(Path.GetFullPath).ToHashSet() ?? [];

        foreach (var path in searchPaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.LinkTarget != null)
            {
                var fullPath = Path.GetFullPath(path);
                if (!allowedSymlinkSet.Contains(fullPath))
                {
                    logger.LogWarning(
                        "Module directory '{ModuleDir}' is a symbolic link not in whitelist. Skipping for security reasons. " +
                        "Add to Modules:AllowedSymlinks in appsettings.json if this is a trusted development path.",
                        path);
                    continue;
                }

                logger.LogInformation("Allowing whitelisted symlink: {Path}", path);
            }

            if (!Directory.Exists(path))
            {
                logger.LogWarning("Module search path not found, skipping: {Path}", path);
                continue;
            }

            logger.LogDebug("Scanning directory for modules: {Path}", path);

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                AttributesToSkip = Debugger.IsAttached
                    ? FileAttributes.System
                    : FileAttributes.System | FileAttributes.ReparsePoint
            };

            if (Debugger.IsAttached)
                logger.LogDebug("Development mode: Symlinked module directories are allowed");

            var moduleDirectories = Directory.EnumerateDirectories(path, "*", enumerationOptions);
            foreach (var moduleDir in moduleDirectories)
            {
                var jsonFile = Path.Combine(moduleDir, "module.json");

                if (!File.Exists(jsonFile)) continue;

                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    const int maxSizeBytes = 10 * 1024; // 10 KB
                    var fileInfo = new FileInfo(jsonFile);

                    if (fileInfo.Length > maxSizeBytes)
                    {
                        logger.LogError(
                            "module.json file exceeds {MaxSize} KB limit: {Path} ({ActualSize} KB). " +
                            "Reduce metadata size or contact support to increase limit. Skipping module",
                            maxSizeBytes / 1024, jsonFile, fileInfo.Length / 1024);
                        continue;
                    }

                    var jsonString = await File.ReadAllTextAsync(jsonFile, cancellationToken).ConfigureAwait(false);

                    var definition = JsonSerializer.Deserialize<ModuleDefinition>(jsonString, _jsonOptions);

                    if (definition == null)
                    {
                        logger.LogWarning(
                            "Failed to deserialize module.json at {JsonPath}, it resulted in a null object",
                            jsonFile);
                        continue;
                    }

                    if (!definition.Platforms.Contains(currentPlatform))
                    {
                        logger.LogDebug(
                            "Skipping module '{ModuleName}' as it does not support the current platform ({Platform})",
                            definition.Name, currentPlatform);
                        continue;
                    }

                    var moduleDirectory = Path.GetDirectoryName(jsonFile);
                    if (moduleDirectory == null) continue;

                    string dllFile;
                    if (!string.IsNullOrEmpty(definition.AssemblyFileName))
                    {
                        dllFile = Path.Combine(moduleDirectory, definition.AssemblyFileName);
                        if (!File.Exists(dllFile))
                        {
                            logger.LogWarning(
                                "Specified assembly '{Assembly}' not found for module '{ModuleName}'. Skipping",
                                definition.AssemblyFileName, definition.Name);
                            continue;
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "Module '{ModuleName}' should specify 'assembly' field in module.json for deterministic loading",
                            definition.Name);
                        continue;
                    }

                    ModuleAssemblyLoadContext? loadContext = null;
                    try
                    {
                        loadContext = new ModuleAssemblyLoadContext(dllFile);
                        var assembly = loadContext.LoadFromAssemblyPath(dllFile);
                        var moduleType = assembly.GetExportedTypes()
                            .FirstOrDefault(t =>
                                typeof(IModule).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

                        if (moduleType != null)
                        {
                            definition.LoadContext = loadContext;
                            definition.ModuleType = moduleType;

                            definitions.Add(definition);

                            logger.LogInformation("Discovered module definition '{ModuleName}' from {DllFile}",
                                definition.Name, Path.GetFileName(dllFile));

                            loadContext = null; // Ownership transferred, don't unload
                        }
                        else
                        {
                            logger.LogWarning(
                                "DLL {DllFile} does not contain a public class implementing IModule. Skipping module '{ModuleName}'",
                                Path.GetFileName(dllFile), definition.Name);
                        }
                    }
                    finally
                    {
                        // Unload if ownership was not transferred
                        if (loadContext != null)
                            try
                            {
                                loadContext.Unload();
                            }
                            catch (Exception unloadEx)
                            {
                                logger.LogError(unloadEx, "Failed to unload assembly context for {DllFile}",
                                    Path.GetFileName(dllFile));
                            }
                    }
                }
                catch (JsonException jsonEx)
                {
                    logger.LogError(jsonEx, "Invalid JSON format in {JsonFile}. Skipping", jsonFile);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load module definition from {JsonFile}", jsonFile);
                }
            }
        }

        logger.LogInformation("Module discovery finished. Found {Count} compatible module definitions",
            definitions.Count);
        return definitions;
    }
}