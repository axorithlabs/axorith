using Autofac;
using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Defines a service that acts as an in-memory registry for all loaded module definitions
///     and is responsible for creating new module instances with isolated dependency scopes.
/// </summary>
public interface IModuleRegistry
{
    /// <summary>
    ///     Gets a collection of all loaded module definitions.
    /// </summary>
    /// <returns>A read-only list of available module definitions.</returns>
    IReadOnlyList<ModuleDefinition> GetAllDefinitions();

    /// <summary>
    ///     Finds a specific module definition by its unique identifier.
    /// </summary>
    /// <param name="moduleId">The GUID of the module definition to find.</param>
    /// <returns>The found <see cref="IModuleDefinition" />, or null if not found.</returns>
    ModuleDefinition? GetDefinitionById(Guid moduleId);

    /// <summary>
    ///     Creates a new, unique runtime instance of a module and its isolated dependency scope.
    /// </summary>
    /// <param name="moduleId">The ID of the module to instantiate.</param>
    /// <returns>A tuple containing the new module instance and its lifetime scope, or (null, null) if creation fails.</returns>
    (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid moduleId);
}