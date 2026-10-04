using Autofac;
using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

public interface IModuleRegistry
{
    IReadOnlyList<ModuleDefinition> GetAllDefinitions();

    ModuleDefinition? GetDefinitionById(Guid moduleId);

    (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid moduleId);
}
