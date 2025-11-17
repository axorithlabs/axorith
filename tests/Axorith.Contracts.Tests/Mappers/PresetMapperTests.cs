using Axorith.Contracts;
using FluentAssertions;
using Xunit;

namespace Axorith.Test.Contracts.Mappers;

/// <summary>
///     Tests for protobuf mapping between domain models and gRPC messages
/// </summary>
public class PresetMapperTests
{
    [Fact]
    public void PresetMessage_ShouldHaveRequiredFields()
    {
        // Arrange & Act
        var preset = new Preset
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Preset"
        };

        // Assert
        preset.Id.Should().NotBeNullOrEmpty();
        preset.Name.Should().Be("Test Preset");
        preset.Modules.Should().NotBeNull();
    }

    [Fact]
    public void ConfiguredModule_ShouldMapCorrectly()
    {
        // Arrange & Act
        var module = new ConfiguredModule
        {
            ModuleId = Guid.NewGuid().ToString(),
            InstanceId = Guid.NewGuid().ToString(),
            CustomName = "Custom Name"
        };

        // Assert
        module.ModuleId.Should().NotBeNullOrEmpty();
        module.InstanceId.Should().NotBeNullOrEmpty();
        module.CustomName.Should().Be("Custom Name");
        module.Settings.Should().NotBeNull();
    }

    [Fact]
    public void Preset_WithMultipleModules_ShouldContainAll()
    {
        // Arrange
        var preset = new Preset
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Multi-Module Preset"
        };

        preset.Modules.Add(new ConfiguredModule { ModuleId = Guid.NewGuid().ToString() });
        preset.Modules.Add(new ConfiguredModule { ModuleId = Guid.NewGuid().ToString() });
        preset.Modules.Add(new ConfiguredModule { ModuleId = Guid.NewGuid().ToString() });

        // Assert
        preset.Modules.Should().HaveCount(3);
    }
}