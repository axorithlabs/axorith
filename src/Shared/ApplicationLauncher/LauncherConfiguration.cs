namespace Axorith.Shared.ApplicationLauncher;

public static class LauncherConfiguration
{
    public static ProcessConfig BuildProcess(
        LauncherSettingsBase settings,
        string arguments = "",
        string? workingDirectory = null)
    {
        var startMode = settings.ProcessMode.GetCurrentValue() switch
        {
            "AttachExisting" => ProcessStartMode.AttachExisting,
            "LaunchOrAttach" => ProcessStartMode.LaunchOrAttach,
            _ => ProcessStartMode.LaunchNew
        };

        return new ProcessConfig(
            settings.ApplicationPath.GetCurrentValue(),
            arguments,
            startMode,
            ParseLifecycleMode(settings.LifecycleMode.GetCurrentValue()),
            workingDirectory);
    }

    public static WindowConfig BuildWindow(LauncherSettingsBase settings, WindowConfigTimings? timings = null)
    {
        timings ??= new WindowConfigTimings();
        var state = settings.WindowState.GetCurrentValue();
        var useCustomSize = settings.UseCustomSize.GetCurrentValue();
        var moveToMonitor = settings.MoveToMonitor.GetCurrentValue();

        int? width = null;
        int? height = null;
        if (useCustomSize && state == "Normal")
        {
            width = settings.WindowWidth.GetCurrentValue();
            height = settings.WindowHeight.GetCurrentValue();
        }

        int? monitorIndex = null;
        if (moveToMonitor && int.TryParse(settings.TargetMonitor.GetCurrentValue(), out var parsedIndex))
        {
            monitorIndex = parsedIndex;
        }

        return new WindowConfig(
            state,
            useCustomSize,
            width,
            height,
            moveToMonitor,
            monitorIndex,
            settings.BringToForeground.GetCurrentValue(),
            timings.WaitForWindowTimeoutMs,
            timings.MoveDelayMs,
            timings.MaximizeSnapDelayMs,
            timings.FinalFocusDelayMs,
            timings.BannerDelayMs);
    }

    public static ProcessLifecycleMode ParseLifecycleMode(string setting) => setting switch
    {
        "KeepRunning" => ProcessLifecycleMode.KeepRunning,
        "TerminateForce" or "TerminateOnEnd" => ProcessLifecycleMode.TerminateForce,
        _ => ProcessLifecycleMode.TerminateGraceful
    };
}
