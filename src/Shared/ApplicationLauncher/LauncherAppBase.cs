using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Shared.ApplicationLauncher;

public abstract class LauncherAppBase(
    IModuleLogger logger,
    IPlatformProcessService processService,
    IPlatformWindowService windowService) : IDisposable
{
    protected IModuleLogger Logger { get; } = logger;

    protected ProcessService ProcessService { get; } = new(logger, processService);

    protected WindowService WindowService { get; } = new(logger, windowService);

    protected Process? CurrentProcess { get; private set; }

    protected bool AttachedToExisting { get; private set; }

    protected abstract LauncherSettingsBase Settings { get; }

    public IReadOnlyList<ISetting> GetSettings() => Settings.GetAllSettings();

    public IReadOnlyList<IAction> GetActions() => Settings.GetAllActions();

    public virtual Task InitializeAsync(CancellationToken cancellationToken) => Settings.InitializeAsync();

    public virtual Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken) => Settings.ValidateAsync();


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
            if (!ReattachOnWindowTimeout)
            {
                Logger.LogWarning("Window did not appear in time. Skipping window configuration.");
                return;
            }

            var fallback = await ProcessService.AttachExistingOnlyAsync(processConfig.ApplicationPath)
                .ConfigureAwait(false);
            if (fallback == null)
            {
                Logger.LogWarning("Window did not appear in time. Skipping window configuration.");
                return;
            }

            CurrentProcess = fallback;
            AttachedToExisting = true;
            await WindowService.ConfigureWindowAsync(CurrentProcess, BuildWindowConfig(), cancellationToken)
                .ConfigureAwait(false);
            await OnAfterWindowConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (CurrentProcess.HasExited)
        {
            Logger.LogDebug("Process exited before window configuration completed");
        }
    }

    public virtual async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentProcess == null || CurrentProcess.HasExited)
        {
            Logger.LogDebug("Process already exited or not started");
            return;
        }

        var lifecycleSetting = Settings.LifecycleMode.GetCurrentValue();
        var lifecycle = LauncherConfiguration.ParseLifecycleMode(lifecycleSetting);

        Logger.LogInfo("Session ending. Lifecycle mode: {Mode}, Attached to existing: {Attached}",
            lifecycleSetting, AttachedToExisting);

        await ProcessService.TerminateAsync(CurrentProcess, lifecycle, AttachedToExisting).ConfigureAwait(false);
    }

    public virtual void Dispose()
    {
        var process = CurrentProcess;
        try
        {
            if (process is { HasExited: false })
            {
                var lifecycleSetting = Settings.LifecycleMode.GetCurrentValue();
                var lifecycle = lifecycleSetting == "KeepRunning"
                    ? ProcessLifecycleMode.KeepRunning
                    : ProcessLifecycleMode.TerminateGraceful;

                ProcessService.TerminateAsync(process, lifecycle, AttachedToExisting).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Swallow exceptions in Dispose
        }
        finally
        {
            process?.Dispose();
            CurrentProcess = null;
            Settings.Dispose();
        }

        GC.SuppressFinalize(this);
    }


    protected virtual ProcessConfig BuildProcessConfig() =>
        LauncherConfiguration.BuildProcess(Settings, GetLaunchArguments(), GetWorkingDirectory());

    protected virtual WindowConfig BuildWindowConfig() =>
        LauncherConfiguration.BuildWindow(Settings, GetWindowConfigTimings());

    protected virtual string GetLaunchArguments() => string.Empty;

    protected virtual string? GetWorkingDirectory() => null;

    protected virtual WindowConfigTimings GetWindowConfigTimings() => new();

    protected virtual bool ReattachOnWindowTimeout => true;

    protected virtual Task OnBeforeWindowConfigurationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task OnAfterWindowConfigurationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

}
