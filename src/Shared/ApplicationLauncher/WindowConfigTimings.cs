namespace Axorith.Shared.ApplicationLauncher;

public sealed record WindowConfigTimings(
    int WaitForWindowTimeoutMs = 7000,
    int MoveDelayMs = 300,
    int MaximizeSnapDelayMs = 250,
    int FinalFocusDelayMs = 250,
    int BannerDelayMs = 0
);
