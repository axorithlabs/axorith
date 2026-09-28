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
    public FocusCommitmentMode Mode { get; set; }
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
