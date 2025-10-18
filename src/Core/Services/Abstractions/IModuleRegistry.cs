using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
/// Defines a service that acts as an in-memory registry for all loaded modules.
/// </summary>
public interface IModuleRegistry
{
    /// <summary>
    /// Gets a list of all modules that have been successfully loaded.
    /// </summary>
    /// <returns>A read-only list of available modules.</returns>
    IReadOnlyList<IModule> GetAllModules();

    /// <summary>
    /// Finds a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The GUID of the module to find.</param>
    /// <returns>The found <see cref="IModule"/> instance, or null if not found.</returns>
    IModule? GetModuleById(Guid moduleId);
}