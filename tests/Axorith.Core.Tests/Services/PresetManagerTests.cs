using Axorith.Core.Models;
using Axorith.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

public class PresetManagerTests : IDisposable
{
    private readonly string _testPresetsDirectory;
    private readonly PresetManager _manager;

    public PresetManagerTests()
    {
        _testPresetsDirectory = Path.Combine(Path.GetTempPath(), $"axorith-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testPresetsDirectory);
        _manager = new PresetManager(_testPresetsDirectory, NullLogger<PresetManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testPresetsDirectory))
        {
            Directory.Delete(_testPresetsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SavePresetAsync_ShouldCreateJsonFile()
    {
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test Preset",
            Version = 1
        };

        await _manager.SavePresetAsync(preset, CancellationToken.None);

        var files = Directory.GetFiles(_testPresetsDirectory, "*.json");
        files.Should().HaveCount(1);
        files[0].Should().EndWith($"{preset.Id}.json");
    }

    [Fact]
    public async Task LoadAllPresetsAsync_WithNoPresets_ShouldReturnEmptyList()
    {
        var presets = await _manager.LoadAllPresetsAsync(CancellationToken.None);

        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllPresetsAsync_WithSavedPreset_ShouldLoadIt()
    {
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Loaded Preset"
        };
        await _manager.SavePresetAsync(preset, CancellationToken.None);

        var presets = await _manager.LoadAllPresetsAsync(CancellationToken.None);

        presets.Should().HaveCount(1);
        presets[0].Name.Should().Be("Loaded Preset");
    }

    [Fact]
    public async Task DeletePresetAsync_ShouldRemoveFile()
    {
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "To Delete"
        };
        await _manager.SavePresetAsync(preset, CancellationToken.None);

        await _manager.DeletePresetAsync(preset.Id, CancellationToken.None);

        var files = Directory.GetFiles(_testPresetsDirectory, "*.json");
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePresetAsync_NonExistentPreset_ShouldNotThrow()
    {
        var randomId = Guid.NewGuid();

        var act = async () => await _manager.DeletePresetAsync(randomId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SavePresetAsync_UpdateExisting_ShouldOverwrite()
    {
        var presetId = Guid.NewGuid();
        var preset1 = new SessionPreset
        {
            Id = presetId,
            Name = "Original Name"
        };
        await _manager.SavePresetAsync(preset1, CancellationToken.None);

        var preset2 = new SessionPreset
        {
            Id = presetId,
            Name = "Updated Name"
        };

        await _manager.SavePresetAsync(preset2, CancellationToken.None);

        var loaded = await _manager.LoadAllPresetsAsync(CancellationToken.None);
        loaded.Should().HaveCount(1);
        loaded[0].Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task LoadAllPresetsAsync_WithMultiplePresets_ShouldLoadAll()
    {
        var preset1 = new SessionPreset { Id = Guid.NewGuid(), Name = "Preset 1" };
        var preset2 = new SessionPreset { Id = Guid.NewGuid(), Name = "Preset 2" };
        var preset3 = new SessionPreset { Id = Guid.NewGuid(), Name = "Preset 3" };

        await _manager.SavePresetAsync(preset1, CancellationToken.None);
        await _manager.SavePresetAsync(preset2, CancellationToken.None);
        await _manager.SavePresetAsync(preset3, CancellationToken.None);

        var presets = await _manager.LoadAllPresetsAsync(CancellationToken.None);

        presets.Should().HaveCount(3);
        presets.Select(p => p.Name).Should().Contain(["Preset 1", "Preset 2", "Preset 3"]);
    }

    [Fact]
    public async Task SavePresetAsync_WithConfiguredModules_ShouldPersistThem()
    {
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "With Modules",
            Modules =
            [
                new ConfiguredModule
                {
                    ModuleId = Guid.NewGuid(),
                    Settings = new Dictionary<string, string>
                    {
                        ["setting1"] = "value1"
                    }
                }
            ]
        };

        await _manager.SavePresetAsync(preset, CancellationToken.None);
        var loaded = await _manager.LoadAllPresetsAsync(CancellationToken.None);

        loaded[0].Modules.Should().HaveCount(1);
        loaded[0].Modules[0].ModuleId.Should().NotBe(Guid.Empty);
        loaded[0].Modules[0].Settings["setting1"].Should().Be("value1");
    }

    [Fact]
    public async Task Cancellation_ShouldRespectCancellationToken()
    {
        await _manager.SavePresetAsync(new SessionPreset { Id = Guid.NewGuid(), Name = "Cancelled load" },
            CancellationToken.None);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _manager.LoadAllPresetsAsync(cts.Token);

        result.Should().BeEmpty("Cancellation should stop loading early");
    }
}
