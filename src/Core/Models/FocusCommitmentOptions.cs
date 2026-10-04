using System.Text.Json.Serialization;

namespace Axorith.Core.Models;

public enum FocusCommitmentMode
{
    Normal,
    Locked,
    Strict
}

public enum FocusEndCondition
{
    None,
    Duration,
    EndAt
}

public enum AfterEndBehavior
{
    DoNothing,
    StartNextWorkspace,
    LockPc,
    Sleep,
    SignOut,
    ShutDownPc
}

public enum SessionEndReason
{
    UserStop,
    NaturalCompletion,
    EmergencyUnlock,
    StartupFailure
}

public sealed class FocusCommitmentOptions
{
    public FocusCommitmentOptions() { }

    public FocusCommitmentOptions(FocusCommitmentOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Mode = source.Mode;
        EndCondition = source.EndCondition;
        Duration = source.Duration;
        EndAtLocalTime = source.EndAtLocalTime;
        EndAtDaysOfWeek = [.. source.EndAtDaysOfWeek];
        BreakCount = source.BreakCount;
        BreakDuration = source.BreakDuration;
        AfterEnd = source.AfterEnd;
        NextWorkspaceId = source.NextWorkspaceId;
        ScheduleLockMinutes = source.ScheduleLockMinutes;
    }

    public FocusCommitmentMode Mode { get; set; }

    [JsonIgnore]
    public bool IsCommitted => Mode is FocusCommitmentMode.Locked or FocusCommitmentMode.Strict;

    [JsonIgnore]
    public bool IsStrict => Mode == FocusCommitmentMode.Strict;
    public FocusEndCondition EndCondition { get; set; }
    public TimeSpan? Duration { get; set; }
    public TimeOnly? EndAtLocalTime { get; set; }
    public List<DayOfWeek> EndAtDaysOfWeek { get; set; } = [];
    public int BreakCount { get; set; }
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(5);
    public AfterEndBehavior AfterEnd { get; set; }
    public Guid? NextWorkspaceId { get; set; }
    public int ScheduleLockMinutes { get; set; }
}
