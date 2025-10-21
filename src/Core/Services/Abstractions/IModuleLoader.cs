using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Defines a service responsible for discovering and loading modules from the file system.
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    ///     Asynchronously loads module definitions from the specified search paths.
    /// </summary>
    /// <param name="searchPaths">The paths to search for modules.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a read-only list of module
    ///     definitions.
    /// </returns>
    Task<IReadOnlyList<ModuleDefinition>> LoadModuleDefinitionsAsync(
        IEnumerable<string> searchPaths, CancellationToken cancellationToken);
}