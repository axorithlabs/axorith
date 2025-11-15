using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Defines a service responsible for discovering and loading modules from the file system.
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    ///     Asynchronously discovers and loads module definitions from the specified search paths.
    /// </summary>
    /// <param name="searchPaths">Directories to scan for modules.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <param name="allowedSymlinks">Optional whitelist of allowed symlink paths. Empty list = no symlinks allowed.</param>
    /// <returns>A list of module definitions found.</returns>
    Task<IReadOnlyList<ModuleDefinition>> LoadModuleDefinitionsAsync(
        IEnumerable<string> searchPaths,
        CancellationToken cancellationToken,
        IEnumerable<string>? allowedSymlinks = null);
}