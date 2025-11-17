using System.Diagnostics;

namespace Axorith.Shared.ApplicationLauncher;

public enum ProcessStartMode
{
    LaunchNew,
    AttachExisting,
    LaunchOrAttach
}

public enum ProcessLifecycleMode
{
    TerminateOnEnd,
    KeepRunning
}

public sealed record ProcessConfig(
    string ApplicationPath,
    string Arguments,
    ProcessStartMode StartMode,
    ProcessLifecycleMode LifecycleMode,
    string? WorkingDirectory
);

public sealed record ProcessStartResult(
    Process? Process,
    bool AttachedToExisting
);
