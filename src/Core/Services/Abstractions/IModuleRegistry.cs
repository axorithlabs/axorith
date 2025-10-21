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
    IReadOnlyList<IModule> GetAllModuleDefs();

    /// <summary>
    /// Finds a specific module by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The GUID of the module to find.</param>
    /// <returns>The found <see cref="IModule"/> instance, or null if not found.</returns>
    IModule? GetModuleDefById(Guid moduleId);

    /// <summary>
    /// Creates a new instance of a module by its ID.
    /// </summary>
    /// <param name="moduleId">The ID of the module to instantiate.</param>
    /// <returns>A new instance of the module, or null if the module type is not found.</returns>
    IModule? CreateModuleInstance(Guid moduleId);
}