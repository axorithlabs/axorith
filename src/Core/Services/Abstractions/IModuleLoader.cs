using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

public interface IModuleLoader
{
    Task<IReadOnlyList<ModuleDefinition>> LoadModuleDefinitionsAsync(
        IEnumerable<string> searchPaths,
        CancellationToken cancellationToken,
        IEnumerable<string>? allowedSymlinks = null);
}
