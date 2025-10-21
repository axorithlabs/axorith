using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
/// Defines a service responsible for discovering and loading modules from the file system.
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    /// Scans the specified directories for module assemblies, loads them,
    /// and returns instantiated module objects.
    /// </summary>
    /// <param name="searchPaths">A collection of directory paths to search for modules.</param>
    /// <param name="cancellationToken">A token to cancel the loading operation.</param>
    /// <returns>A read-only list of all successfully loaded and validated modules.</returns>
    public Task<(IReadOnlyList<IModule> Definitions, IReadOnlyDictionary<Guid, Type> Types)> LoadModuleTypesAsync(
        IEnumerable<string> searchPaths, CancellationToken cancellationToken);
}