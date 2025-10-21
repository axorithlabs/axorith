using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
/// Defines a service that acts as an in-memory registry for all loaded module definitions and types.
/// It separates the static definition of a module (metadata) from its runtime instance.
/// </summary>
public interface IModuleRegistry
{
    /// <summary>
    /// Gets a collection of all loaded module definitions.
    /// These are lightweight, shared instances used to read metadata (Name, Settings, etc.).
    /// </summary>
    /// <returns>A read-only list of available module definitions.</returns>
    IReadOnlyList<IModule> GetAllDefinitions();

    /// <summary>
    /// Finds a specific module definition by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The GUID of the module definition to find.</param>
    /// <returns>The found <see cref="IModule"/> definition instance, or null if not found.</returns>
    IModule? GetDefinitionById(Guid moduleId);

    /// <summary>
    /// Creates a new, unique runtime instance of a module by its ID.
    /// Each call to this method should return a new object.
    /// </summary>
    /// <param name="moduleId">The ID of the module to instantiate.</param>
    /// <returns>A new instance of the module implementing <see cref="IModule"/>, or null if the module type is not found.</returns>
    IModule? CreateInstance(Guid moduleId);
}