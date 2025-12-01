using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

/// <summary>
///     Tests for PresetsServiceImpl - gRPC service for preset management
/// </summary>
public class PresetsServiceImplTests
{
    private readonly Mock<IPresetManager> _mockPresetManager;
    private readonly Mock<IDesignTimeSandboxManager> _sandboxManager;
    private readonly PresetsServiceImpl _service;

    public PresetsServiceImplTests()
    {
        _mockPresetManager = new Mock<IPresetManager>();
        _sandboxManager = new Mock<IDesignTimeSandboxManager>();
        _service = new PresetsServiceImpl(
            _mockPresetManager.Object,
            _sandboxManager.Object,
            NullLogger<PresetsServiceImpl>.Instance
        );
    }

    private static ServerCallContext CreateTestContext()
    {
        return TestServerCallContext.Create(
            method: "test",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: [],
            cancellationToken: CancellationToken.None,
            peer: "127.0.0.1",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => new WriteOptions(),
            writeOptionsSetter: _ => { }
        );
    }

    #region ListPresets Tests

    [Fact]
    public async Task ListPresets_WithNoPresets_ShouldReturnEmptyList()
    {
        // Arrange
        _mockPresetManager.Setup(m => m.LoadAllPresetsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionPreset>());

        var request = new ListPresetsRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.ListPresets(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Presets.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPresets_WithMultiplePresets_ShouldReturnAll()
    {
        // Arrange
        var presets = new List<SessionPreset>
        {
            new() { Id = Guid.NewGuid(), Name = "Preset 1", Modules = [] },
            new() { Id = Guid.NewGuid(), Name = "Preset 2", Modules = [] },
            new() { Id = Guid.NewGuid(), Name = "Preset 3", Modules = [] }
        };

        _mockPresetManager.Setup(m => m.LoadAllPresetsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(presets);

        var request = new ListPresetsRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.ListPresets(request, context);

        // Assert
        response.Presets.Should().HaveCount(3);
        response.Presets.Should().Contain(p => p.Name == "Preset 1");
        response.Presets.Should().Contain(p => p.Name == "Preset 2");
        response.Presets.Should().Contain(p => p.Name == "Preset 3");
    }

    #endregion

    #region GetPreset Tests

    [Fact]
    public async Task GetPreset_WithValidId_ShouldReturnPreset()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Preset",
            Modules = []
        };

        // GetPreset uses LoadAllPresetsAsync internally
        _mockPresetManager.Setup(m => m.LoadAllPresetsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionPreset> { preset });

        var request = new GetPresetRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        var response = await _service.GetPreset(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Name.Should().Be("Test Preset");
    }

    [Fact]
    public async Task GetPreset_WithInvalidId_ShouldThrowRpcException()
    {
        // Arrange
        var request = new GetPresetRequest { PresetId = "invalid-guid" };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.GetPreset(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetPreset_WhenNotFound_ShouldThrowRpcException()
    {
        // Arrange
        var presetId = Guid.NewGuid();

        // Return empty list - preset not found
        _mockPresetManager.Setup(m => m.LoadAllPresetsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionPreset>());

        var request = new GetPresetRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.GetPreset(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.NotFound);
    }

    #endregion

    #region CreatePreset Tests

    [Fact]
    public async Task CreatePreset_WithValidData_ShouldCreatePreset()
    {
        // Arrange
        var request = new CreatePresetRequest
        {
            Preset = new Preset
            {
                Name = "New Preset"
            }
        };

        _mockPresetManager.Setup(m => m.SavePresetAsync(It.IsAny<SessionPreset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var context = CreateTestContext();

        // Act
        var response = await _service.CreatePreset(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Name.Should().Be("New Preset");
        _mockPresetManager.Verify(m => m.SavePresetAsync(It.IsAny<SessionPreset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreatePreset_WithoutId_ShouldGenerateId()
    {
        // Arrange
        var request = new CreatePresetRequest
        {
            Preset = new Preset { Name = "New Preset" }
        };

        _mockPresetManager.Setup(m => m.SavePresetAsync(It.IsAny<SessionPreset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var context = CreateTestContext();

        // Act
        var response = await _service.CreatePreset(request, context);

        // Assert
        response.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(response.Id, out _).Should().BeTrue();
    }

    #endregion

    #region DeletePreset Tests

    [Fact]
    public async Task DeletePreset_WithValidId_ShouldDeletePreset()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        _mockPresetManager.Setup(m => m.DeletePresetAsync(presetId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new DeletePresetRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        await _service.DeletePreset(request, context);

        // Assert
        _mockPresetManager.Verify(m => m.DeletePresetAsync(presetId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePreset_WithInvalidId_ShouldThrowRpcException()
    {
        // Arrange
        var request = new DeletePresetRequest { PresetId = "invalid-guid" };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.DeletePreset(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    #endregion
}