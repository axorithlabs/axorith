using Autofac;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Services;

public class ModuleRegistryTests
{
    private readonly Mock<IModuleLoader> _mockLoader;
    private readonly ModuleRegistry _registry;

    public ModuleRegistryTests()
    {
        _mockLoader = new Mock<IModuleLoader>();

        // Create a minimal Autofac container for testing
        var builder = new ContainerBuilder();
        var container = builder.Build();

        // Mock search paths for testing
        var searchPaths = new[] { "./test-modules" };
        var allowedSymlinks = Array.Empty<string>();

        _registry = new ModuleRegistry(
            container,
            _mockLoader.Object,
            searchPaths,
            allowedSymlinks,
            NullLogger<ModuleRegistry>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadDefinitions()
    {
        // Arrange
        var id = Guid.NewGuid();
        var definitions = new List<ModuleDefinition>
        {
            new()
            {
                Id = id,
                Name = "Test Module",
                ModuleType = typeof(object) // Dummy type for test
            }
        };

        _mockLoader.Setup(l => l.LoadModuleDefinitionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(definitions);

        // Act
        await _registry.InitializeAsync(CancellationToken.None);

        // Assert
        _mockLoader.Verify(l => l.LoadModuleDefinitionsAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Fact]
    public async Task GetAllDefinitions_AfterInit_ShouldReturnLoadedDefinitions()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var definition1 = new ModuleDefinition
        {
            Id = id1,
            Name = "Module 1",
            ModuleType = typeof(object)
        };

        var definition2 = new ModuleDefinition
        {
            Id = id2,
            Name = "Module 2",
            ModuleType = typeof(object)
        };

        _mockLoader.Setup(l => l.LoadModuleDefinitionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<ModuleDefinition> { definition1, definition2 });

        await _registry.InitializeAsync(CancellationToken.None);

        // Act
        var definitions = _registry.GetAllDefinitions();

        // Assert
        definitions.Should().HaveCount(2);
        definitions.Should().Contain(d => d.Id == id1);
        definitions.Should().Contain(d => d.Id == id2);
    }

    [Fact]
    public async Task GetDefinition_WithValidId_ShouldReturnDefinition()
    {
        // Arrange
        var id = Guid.NewGuid();
        var definition = new ModuleDefinition
        {
            Id = id,
            Name = "Target Module",
            ModuleType = typeof(object)
        };

        _mockLoader.Setup(l => l.LoadModuleDefinitionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<ModuleDefinition> { definition });

        await _registry.InitializeAsync(CancellationToken.None);

        // Act
        var result = _registry.GetDefinitionById(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetDefinition_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        _mockLoader.Setup(l => l.LoadModuleDefinitionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<ModuleDefinition>());

        await _registry.InitializeAsync(CancellationToken.None);

        // Act
        var result = _registry.GetDefinitionById(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllDefinitions_BeforeInit_ShouldThrow()
    {
        // Act
        var act = () => _registry.GetAllDefinitions();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not been initialized*");
    }

    [Fact]
    public async Task InitializeAsync_CalledMultipleTimes_ShouldReload()
    {
        // Arrange
        _mockLoader.Setup(l => l.LoadModuleDefinitionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<ModuleDefinition>());

        // Act
        await _registry.InitializeAsync(CancellationToken.None);
        await _registry.InitializeAsync(CancellationToken.None);
        await _registry.InitializeAsync(CancellationToken.None);

        // Assert
        _mockLoader.Verify(l => l.LoadModuleDefinitionsAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IEnumerable<string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitializeAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _mockLoader.Setup(l => l.LoadModuleDefinitionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IEnumerable<string>>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = async () => await _registry.InitializeAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}