namespace Axorith.Shared.ApplicationLauncher;

public sealed record WindowConfig(
    string State,
    bool UseCustomSize,
    int? Width,
    int? Height,
    bool MoveToMonitor,
    int? TargetMonitorIndex,
    bool BringToForeground,
    int WaitForWindowTimeoutMs = 7000,
    int MoveDelayMs = 300,
    int MaximizeSnapDelayMs = 250,
    int FinalFocusDelayMs = 250,
    int BannerDelayMs = 0
);