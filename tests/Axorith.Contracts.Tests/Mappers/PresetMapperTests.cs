using Axorith.Host.Mappers;
using FluentAssertions;
using Xunit;

namespace Axorith.Contracts.Tests.Mappers;

public sealed class PresetMapperTests
{
    [Fact]
    public void PresetRoundTripPreservesModuleConfigurationAndCommitment()
    {
        var presetId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var moduleInstanceId = Guid.NewGuid();
        var nextPresetId = Guid.NewGuid();
        var preset = new global::Axorith.Core.Models.SessionPreset
        {
            Id = presetId,
            Name = "Deep work",
            FocusCommitment = new global::Axorith.Core.Models.FocusCommitmentOptions
            {
                Mode = global::Axorith.Core.Models.FocusCommitmentMode.Locked,
                EndCondition = global::Axorith.Core.Models.FocusEndCondition.EndAt,
                EndAtLocalTime = new TimeOnly(18, 30),
                EndAtDaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Friday],
                BreakCount = 2,
                BreakDuration = TimeSpan.FromMinutes(10),
                AfterEnd = global::Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace,
                NextWorkspaceId = nextPresetId,
                ScheduleLockMinutes = 15
            },
            Modules =
            [
                new global::Axorith.Core.Models.ConfiguredModule
                {
                    ModuleId = moduleId,
                    InstanceId = moduleInstanceId,
                    CustomName = "Focus blocker",
                    StartDelay = TimeSpan.FromSeconds(2.5),
                    Settings = new Dictionary<string, string> { ["Mode"] = "AllowList", ["CustomApps"] = "editor" }
                }
            ]
        };

        var message = PresetMapper.ToMessage(preset);
        var result = PresetMapper.ToModel(message);
        var module = result.Modules.Should().ContainSingle().Which;

        result.Id.Should().Be(presetId);
        result.Name.Should().Be("Deep work");
        module.ModuleId.Should().Be(moduleId);
        module.InstanceId.Should().Be(moduleInstanceId);
        module.CustomName.Should().Be("Focus blocker");
        module.StartDelay.Should().Be(TimeSpan.FromSeconds(2.5));
        module.Settings.Should().BeEquivalentTo(preset.Modules[0].Settings);
        result.FocusCommitment.EndAtLocalTime.Should().Be(new TimeOnly(18, 30));
        result.FocusCommitment.EndAtDaysOfWeek.Should().Equal(DayOfWeek.Monday, DayOfWeek.Friday);
        result.FocusCommitment.BreakDuration.Should().Be(TimeSpan.FromMinutes(10));
        result.FocusCommitment.NextWorkspaceId.Should().Be(nextPresetId);
        PresetMapper.ToSummary(result).ModuleCount.Should().Be(1);
    }

    [Fact]
    public void InvalidModuleIdInIncomingPresetIsRejected()
    {
        var message = new Axorith.Contracts.Preset { Id = Guid.NewGuid().ToString(), Name = "Invalid" };
        message.Modules.Add(new Axorith.Contracts.ConfiguredModule { ModuleId = "not-a-guid" });

        var act = () => PresetMapper.ToModel(message);

        act.Should().Throw<ArgumentException>().WithMessage("*Invalid ModuleId*");
    }
}
