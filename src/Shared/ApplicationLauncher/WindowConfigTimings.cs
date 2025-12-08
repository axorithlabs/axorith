namespace Axorith.Shared.ApplicationLauncher;

/// <summary>
/// Timing configuration for window setup operations.
/// Used by LauncherModuleBase to customize delays for different application types.
/// </summary>
public sealed record WindowConfigTimings(
    int WaitForWindowTimeoutMs = 7000,
    int MoveDelayMs = 300,
    int MaximizeSnapDelayMs = 250,
    int FinalFocusDelayMs = 250,
    int BannerDelayMs = 0
);
