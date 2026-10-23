using Autofac;
using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

public interface IModuleRegistry
{
    IReadOnlyList<ModuleDefinition> GetAllDefinitions();

    ModuleDefinition? GetDefinitionById(Guid moduleId);

    // The returned scope owns the module and its dependencies; dispose the scope once after use.
    (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid moduleId);
}
