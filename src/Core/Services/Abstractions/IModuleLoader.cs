using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
/// Defines a service responsible for discovering and loading modules from the file system.
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    /// Scans the specified directories for module assemblies, validates them, and extracts their types and definitions.
    /// This method does not return long-lived instances; it creates temporary ones to read metadata.
    /// </summary>
    /// <param name="searchPaths">A collection of directory paths to search for modules.</param>
    /// <param name="cancellationToken">A token to cancel the loading operation.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a tuple with:
    /// <list type="bullet">
    /// <item>
    /// <term>Definitions</term>
    /// <description>A read-only list of temporary module instances, used to read metadata like Name, Description, and Settings.</description>
    /// </item>
    /// <item>
    /// <term>Types</term>
    /// <description>A read-only dictionary mapping each module's ID to its loadable <see cref="System.Type"/>, used for creating new instances later.</description>
    /// </item>
    /// </list>
    /// </returns>
    public Task<(IReadOnlyList<IModule> Definitions, IReadOnlyDictionary<Guid, Type> Types)> LoadModuleTypesAsync(
        IEnumerable<string> searchPaths, CancellationToken cancellationToken);
}