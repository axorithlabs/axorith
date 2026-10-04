using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Services;

internal static class SessionManagerTestFactory
{
    public static Mock<IModule> CreateModule()
    {
        var module = new Mock<IModule>();
        module.Setup(m => m.GetSettings()).Returns([]);
        module.Setup(m => m.GetActions()).Returns([]);
        module.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        module.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        module.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        module.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ValidationResult.Success);
        return module;
    }

    public static SessionManager CreateManager(IModuleRegistry registry, ITelemetryService? telemetry = null) => new(
        registry,
        NullLogger<SessionManager>.Instance,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(10),
        telemetry ?? NoopTelemetryService.Instance);

    public static Guid Register(Mock<IModuleRegistry> registry, Mock<IModule> module, Guid? id = null,
        string name = "Test")
    {
        var moduleId = id ?? Guid.NewGuid();
        var definition = new ModuleDefinition
        {
            Id = moduleId,
            Name = name,
            Platforms = [Platform.Windows],
            ModuleType = module.Object.GetType()
        };
        var builder = new ContainerBuilder();
        builder.RegisterInstance(definition).As<ModuleDefinition>();
        var scope = builder.Build();
        registry.Setup(r => r.CreateInstance(moduleId)).Returns((module.Object, scope));
        return moduleId;
    }

    public static SessionPreset CreatePreset(params Guid[] moduleIds) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Modules = [.. moduleIds.Select(id => new ConfiguredModule { ModuleId = id })]
    };
}
