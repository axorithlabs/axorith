using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;

namespace Axorith.Shared.ApplicationLauncher;

/// <summary>
///     Abstract base class for launcher modules.
///     Provides complete lifecycle management for launching applications and configuring windows.
///     Derived classes must provide Settings and can override extension points for customization.
/// </summary>
public abstract class LauncherModuleBase(IModuleLogger logger) : IModule
{
    /// <summary>
    ///     Logger instance for the module.
    /// </summary>
    protected IModuleLogger Logger { get; } = logger;

    /// <summary>
    ///     Service for process management (start, attach, terminate).
    /// </summary>
    protected ProcessService ProcessService { get; } = new(logger);

    /// <summary>
    ///     Service for window configuration (size, position, state).
    /// </summary>
    protected WindowService WindowService { get; } = new(logger);

    /// <summary>
    ///     Current process handle. Null if not started.
    /// </summary>
    protected Process? CurrentProcess { get; private set; }

    /// <summary>
    ///     True if the process was attached from an existing instance rather than launched.
    /// </summary>
    protected bool AttachedToExisting { get; private set; }

    /// <summary>
    ///     Settings instance for this module. Must be implemented by derived classes.
    /// </summary>
    protected abstract LauncherSettingsBase Settings { get; }

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return Settings.GetAllSettings();
    }

    /// <inheritdoc />
    public IReadOnlyList<IAction> GetActions()
    {
        return Settings.GetAllActions();
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Settings.InitializeAsync();
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return Settings.ValidateAsync();
    }


    /// <inheritdoc />
    public virtual async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var processConfig = BuildProcessConfig();

        Logger.LogInfo("Starting in {Mode} mode for {AppPath}",
            processConfig.StartMode, processConfig.ApplicationPath);

        var startResult = await ProcessService.StartAsync(processConfig).ConfigureAwait(false);
        CurrentProcess = startResult.Process;
        AttachedToExisting = startResult.AttachedToExisting;

        if (CurrentProcess == null)
        {
            Logger.LogError(null, "Failed to obtain process handle");
            return;
        }

        try
        {
            await OnBeforeWindowConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var windowConfig = BuildWindowConfig();
            await WindowService.ConfigureWindowAsync(CurrentProcess, windowConfig, cancellationToken)
                .ConfigureAwait(false);

            await OnAfterWindowConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var fallback = await ProcessService.AttachExistingOnlyAsync(
                processConfig.ApplicationPath).ConfigureAwait(false);

            if (fallback == null)
            {
                Logger.LogWarning("Window did not appear in time. Skipping window configuration.");
                return;
            }

            CurrentProcess = fallback;
            AttachedToExisting = true;

            var windowConfig = BuildWindowConfig();
            await WindowService.ConfigureWindowAsync(CurrentProcess, windowConfig, cancellationToken)
                .ConfigureAwait(false);

            await OnAfterWindowConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public virtual async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentProcess == null || CurrentProcess.HasExited)
        {
            Logger.LogDebug("Process already exited or not started");
            return;
        }

        var lifecycleSetting = Settings.LifecycleMode.GetCurrentValue();
        var lifecycle = ParseLifecycleMode(lifecycleSetting);

        Logger.LogInfo("Session ending. Lifecycle mode: {Mode}, Attached to existing: {Attached}",
            lifecycleSetting, AttachedToExisting);

        await ProcessService.TerminateAsync(CurrentProcess, lifecycle, AttachedToExisting).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        try
        {
            if (CurrentProcess is { HasExited: false })
            {
                var lifecycleSetting = Settings.LifecycleMode.GetCurrentValue();
                var lifecycle = lifecycleSetting == "KeepRunning"
                    ? ProcessLifecycleMode.KeepRunning
                    : ProcessLifecycleMode.TerminateGraceful;

                _ = Task.Run(() => ProcessService.TerminateAsync(CurrentProcess, lifecycle, AttachedToExisting));
            }
        }
        catch
        {
            // Swallow exceptions in Dispose
        }
        finally
        {
            CurrentProcess?.Dispose();
            CurrentProcess = null;
        }

        GC.SuppressFinalize(this);
    }


    /// <summary>
    ///     Builds the process configuration. Override to customize process startup.
    /// </summary>
    protected virtual ProcessConfig BuildProcessConfig()
    {
        var appPath = Settings.ApplicationPath.GetCurrentValue();
        var args = GetLaunchArguments();
        var workingDir = GetWorkingDirectory();

        var startMode = Settings.ProcessMode.GetCurrentValue() switch
        {
            "AttachExisting" => ProcessStartMode.AttachExisting,
            "LaunchOrAttach" => ProcessStartMode.LaunchOrAttach,
            _ => ProcessStartMode.LaunchNew
        };

        var lifecycleMode = ParseLifecycleMode(Settings.LifecycleMode.GetCurrentValue());

        return new ProcessConfig(appPath, args, startMode, lifecycleMode, workingDir);
    }

    /// <summary>
    ///     Builds the window configuration. Override to customize window setup.
    /// </summary>
    protected virtual WindowConfig BuildWindowConfig()
    {
        var state = Settings.WindowState.GetCurrentValue();
        var useCustomSize = Settings.UseCustomSize.GetCurrentValue();
        int? width = null;
        int? height = null;

        if (useCustomSize && state == "Normal")
        {
            width = Settings.WindowWidth.GetCurrentValue();
            height = Settings.WindowHeight.GetCurrentValue();
        }

        var moveToMonitor = Settings.MoveToMonitor.GetCurrentValue();
        int? targetMonitorIndex = null;

        if (moveToMonitor)
        {
            var monitorKey = Settings.TargetMonitor.GetCurrentValue();
            if (!string.IsNullOrWhiteSpace(monitorKey) && int.TryParse(monitorKey, out var parsedIndex))
            {
                targetMonitorIndex = parsedIndex;
            }
        }

        var bringToForeground = Settings.BringToForeground.GetCurrentValue();
        var timings = GetWindowConfigTimings();

        return new WindowConfig(
            state,
            useCustomSize,
            width,
            height,
            moveToMonitor,
            targetMonitorIndex,
            bringToForeground,
            timings.WaitForWindowTimeoutMs,
            timings.MoveDelayMs,
            timings.MaximizeSnapDelayMs,
            timings.FinalFocusDelayMs,
            timings.BannerDelayMs);
    }

    /// <summary>
    ///     Gets launch arguments for the process. Override to add custom arguments.
    /// </summary>
    protected virtual string GetLaunchArguments()
    {
        return string.Empty;
    }

    /// <summary>
    ///     Gets working directory for the process. Override to customize.
    ///     Returns null to use default (executable's directory).
    /// </summary>
    protected virtual string? GetWorkingDirectory()
    {
        return null;
    }

    /// <summary>
    ///     Gets timing configuration for window setup. Override for slow-starting applications.
    /// </summary>
    protected virtual WindowConfigTimings GetWindowConfigTimings()
    {
        return new WindowConfigTimings();
    }

    /// <summary>
    ///     Called before window configuration. Override for pre-configuration logic (e.g., splash screen waiting).
    /// </summary>
    protected virtual Task OnBeforeWindowConfigurationAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called after window configuration. Override for post-configuration logic.
    /// </summary>
    protected virtual Task OnAfterWindowConfigurationAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static ProcessLifecycleMode ParseLifecycleMode(string setting)
    {
        return setting switch
        {
            "KeepRunning" => ProcessLifecycleMode.KeepRunning,
            "TerminateForce" => ProcessLifecycleMode.TerminateForce,
            "TerminateOnEnd" => ProcessLifecycleMode.TerminateForce, // Backward compatibility
            _ => ProcessLifecycleMode.TerminateGraceful
        };
    }
}