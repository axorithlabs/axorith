using System.Text.Json;
using System.Security.Principal;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Shared.Platform;
using Microsoft.Win32;

// Windows registry calls are reached only after explicit OperatingSystem.IsWindows() guards.
#pragma warning disable CA1416

namespace Axorith.Host.Services;

public sealed class WindowsCommitmentProtectionService(string stateDirectory,
    ILogger<WindowsCommitmentProtectionService> logger, bool allowLegacyState = false) : ICommitmentProtectionService
{
    private sealed record SavedValue(string KeyPath, string Name, bool KeyExisted, bool Exists,
        RegistryValueKind Kind = RegistryValueKind.String, long Number = 0, string? Text = null,
        string[]? Strings = null, string? Binary = null);

    private sealed record SavedState(IReadOnlyList<SavedValue> Values);

    private static readonly string SystemPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private static readonly string WindowsPolicy = @"Software\Policies\Microsoft\Windows\System";
    private static readonly string ExplorerPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private static readonly string DisallowRunPolicy = ExplorerPolicy + @"\DisallowRun";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Axorith.CommittedSessionRecovery";
    private static readonly string[] BlockedExecutables =
    [
        "cmd.exe", "powershell.exe", "pwsh.exe", "WindowsTerminal.exe", "regedit.exe", "taskmgr.exe",
        "control.exe", "SystemSettings.exe", "SystemSettingsAdminFlows.exe", "mmc.exe",
        "taskkill.exe", "resmon.exe", "reg.exe", "msiexec.exe", "winget.exe", "Axorith.Uninstaller.exe",
        "uninstall.exe", "unins000.exe", "uninstaller.exe", "RevoUnin.exe", "IObitUninstaller.exe",
        "Geek.exe", "BCUninstaller.exe", "UninstallTool.exe"
    ];
    private static readonly string[] BlockedProcessNames =
    [
        .. BlockedExecutables.Select(executable => Path.GetFileNameWithoutExtension(executable)!),
        "wt"
    ];

    private readonly string _statePath = Path.Combine(stateDirectory, "strict-protection.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IProcessBlocker? _processBlocker;
    private bool _protectionStateRequired;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Task CheckCanEnableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Strict Windows protection is available only on Windows.");
        }

        try
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                throw new InvalidOperationException("Strict protection requires Axorith Host to run as administrator.");
            }

            using var softwareKey = Registry.CurrentUser.OpenSubKey("Software", writable: true);
            _ = softwareKey ?? throw new InvalidOperationException("The current user's registry is not writable.");
            if (File.Exists(_statePath))
            {
                var state = JsonSerializer.Deserialize<SavedState>(
                    CommittedSessionStateFile.ReadPayload(_statePath), JsonOptions);
                if (state == null || state.Values == null || state.Values.Count == 0)
                {
                    throw new InvalidDataException("Saved Windows protection state is empty.");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Axorith cannot write the current user's Windows policies: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    public Task CheckRecoveryStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_statePath))
        {
            throw new InvalidDataException("The saved Windows policy snapshot is missing; Strict recovery cannot continue safely.");
        }

        var state = JsonSerializer.Deserialize<SavedState>(ReadStatePayload(), JsonOptions);
        if (state?.Values is not { Count: > 0 })
        {
            throw new InvalidDataException("The saved Windows policy snapshot is invalid.");
        }

        return Task.CompletedTask;
    }

    public Task CheckCanSetRecoveryStartupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            _ = key ?? throw new InvalidOperationException("The current user's startup settings are not writable.");
            if (IsRecoveryStartupEntryInUse(key.GetValue(RunValueName)))
            {
                throw new InvalidOperationException("The Axorith recovery startup entry is already in use.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Axorith cannot register recovery at Windows sign-in: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    public Task SetRecoveryStartupAsync(bool active, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        try
        {
            if (active)
            {
                var executablePath = Path.Combine(AppContext.BaseDirectory, "Axorith.Host.exe");
                if (!File.Exists(executablePath))
                {
                    throw new InvalidOperationException($"The Host executable could not be found at '{executablePath}'.");
                }

                var recoveryCommand = $"\"{Path.GetFullPath(executablePath)}\"";
                using var existingKey = Registry.CurrentUser.OpenSubKey(RunKey);
                var existingValue = existingKey?.GetValue(RunValueName);
                if (IsRecoveryStartupEntryInUse(existingValue) && !IsRecoveryStartupCommand(existingValue))
                {
                    throw new InvalidOperationException("The Axorith recovery startup entry is already in use.");
                }

                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                                ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
                key.SetValue(RunValueName, recoveryCommand, RegistryValueKind.String);
            }
            else
            {
                using var existingKey = Registry.CurrentUser.OpenSubKey(RunKey);
                if (!IsRecoveryStartupCommand(existingKey?.GetValue(RunValueName)))
                {
                    return Task.CompletedTask;
                }

                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
                key.DeleteValue(RunValueName);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(active
                ? $"Axorith could not enable recovery at Windows sign-in: {ex.Message}"
                : $"Axorith could not remove its temporary Windows sign-in entry: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    public async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        await CheckCanEnableAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = File.Exists(_statePath)
                ? ReadState(cancellationToken)
                : CaptureState();

            if (!File.Exists(_statePath))
            {
                SaveState(state, cancellationToken);
            }

            _protectionStateRequired = true;

            SetDword(SystemPolicy, "DisableTaskMgr", 1);
            SetDword(WindowsPolicy, "DisableCMD", 2);
            SetDword(ExplorerPolicy, "NoRun", 1);
            SetDword(ExplorerPolicy, "DisallowRun", 1);
            using var list = Registry.CurrentUser.CreateSubKey(DisallowRunPolicy, writable: true)
                             ?? throw new InvalidOperationException("Cannot create the Windows application restriction list.");
            for (var i = 0; i < BlockedExecutables.Length; i++)
            {
                list.SetValue($"Axorith_{i}", BlockedExecutables[i], RegistryValueKind.String);
            }

            _processBlocker ??= PlatformServices.CreateProcessBlocker(logger);
            _processBlocker.Block(BlockedProcessNames);
            NotifyPolicyChanged();
            logger.LogInformation("Enabled reversible current-user Windows restrictions for a Strict session.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Axorith could not enable Windows protection: {ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var restored = false;
        try
        {
            if (!File.Exists(_statePath))
            {
                if (_protectionStateRequired)
                {
                    throw new InvalidDataException(
                        "The saved Windows policy snapshot is missing; refusing to discard Strict protection.");
                }

                return;
            }

            var state = ReadState(cancellationToken);
            foreach (var saved in state.Values)
            {
                using var key = Registry.CurrentUser.OpenSubKey(saved.KeyPath, writable: true);
                if (key == null)
                {
                    if (saved.Exists)
                    {
                        throw new InvalidOperationException($"Windows policy key '{saved.KeyPath}' is unavailable for restoration.");
                    }

                    continue;
                }

                if (!saved.Exists)
                {
                    key.DeleteValue(saved.Name, throwOnMissingValue: false);
                }
                else
                {
                    var value = saved.Kind switch
                    {
                        RegistryValueKind.DWord => (object)(int)saved.Number,
                        RegistryValueKind.QWord => saved.Number,
                        RegistryValueKind.MultiString => saved.Strings ?? [],
                        RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(saved.Binary ?? string.Empty),
                        _ => saved.Text ?? string.Empty
                    };
                    key.SetValue(saved.Name, value, saved.Kind);
                }
            }

            foreach (var path in state.Values.Where(v => !v.KeyExisted).Select(v => v.KeyPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length))
            {
                using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
                if (key is { ValueCount: 0, SubKeyCount: 0 })
                {
                    Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
                }
            }

            NotifyPolicyChanged();
            File.Delete(_statePath);
            _protectionStateRequired = false;
            restored = true;
            logger.LogInformation("Restored original current-user Windows policy values after Strict session.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to restore temporary Strict session Windows policies; state is retained for retry.");
            throw new InvalidOperationException($"Axorith could not restore Windows protection: {ex.Message}", ex);
        }
        finally
        {
            try
            {
                if (restored)
                {
                    try
                    {
                        _processBlocker?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to stop Strict process monitoring after restoring Windows policies.");
                    }

                    _processBlocker = null;
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public Task ReconcileAsync(bool strictSessionActive, CancellationToken cancellationToken = default) =>
        strictSessionActive ? EnableAsync(cancellationToken) :
        File.Exists(_statePath) ? RestoreAsync(cancellationToken) : Task.CompletedTask;

    private static string GetRecoveryCommand() =>
        $"\"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Axorith.Host.exe"))}\"";

    private static bool IsRecoveryStartupEntryInUse(object? value) => value is not null;

    private static bool IsRecoveryStartupCommand(object? value) =>
        value is string command && string.Equals(command, GetRecoveryCommand(), StringComparison.OrdinalIgnoreCase);

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !File.Exists(_statePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            return Task.FromResult(_processBlocker?.IsMonitoring == true &&
                                   ReadDword(SystemPolicy, "DisableTaskMgr") == 1 &&
                                   ReadDword(WindowsPolicy, "DisableCMD") == 2 &&
                                   ReadDword(ExplorerPolicy, "NoRun") == 1 &&
                                   ReadDword(ExplorerPolicy, "DisallowRun") == 1 &&
                                   HasBlockedExecutableList());
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private SavedState CaptureState()
    {
        var values = new List<SavedValue>();
        Capture(SystemPolicy, "DisableTaskMgr");
        Capture(WindowsPolicy, "DisableCMD");
        Capture(ExplorerPolicy, "NoRun");
        Capture(ExplorerPolicy, "DisallowRun");
        for (var i = 0; i < BlockedExecutables.Length; i++)
        {
            Capture(DisallowRunPolicy, $"Axorith_{i}");
        }

        return new SavedState(values);

        void Capture(string keyPath, string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            var keyExisted = key != null;
            if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(new SavedValue(keyPath, name, keyExisted, false));
                return;
            }

            var kind = key.GetValueKind(name);
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            values.Add(kind switch
            {
                RegistryValueKind.DWord => new SavedValue(keyPath, name, keyExisted, true, kind,
                    Convert.ToInt32(value)),
                RegistryValueKind.QWord => new SavedValue(keyPath, name, keyExisted, true, kind,
                    Convert.ToInt64(value)),
                RegistryValueKind.MultiString => new SavedValue(keyPath, name, keyExisted, true, kind, 0,
                    null, value as string[]),
                RegistryValueKind.Binary or RegistryValueKind.None => new SavedValue(keyPath, name, keyExisted,
                    true, kind, 0, null, null, Convert.ToBase64String(value as byte[] ?? [])),
                _ => new SavedValue(keyPath, name, keyExisted, true, kind, 0, value as string ?? string.Empty)
            });
        }
    }

    private SavedState ReadState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<SavedState>(ReadStatePayload(), JsonOptions)
            ?? throw new InvalidDataException("Saved Windows protection state is empty.");
    }

    private string ReadStatePayload() => allowLegacyState
        ? CommittedSessionStateFile.ReadPayloadOrLegacy(_statePath)
        : CommittedSessionStateFile.ReadPayload(_statePath);

    private void SaveState(SavedState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommittedSessionStateFile.WritePayload(_statePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static void SetDword(string keyPath, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
                        ?? throw new InvalidOperationException($"Cannot open Windows policy key '{keyPath}'.");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static int? ReadDword(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name) is int value ? value : null;
    }

    private static bool HasBlockedExecutableList()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DisallowRunPolicy);
        if (key == null)
        {
            return false;
        }

        for (var i = 0; i < BlockedExecutables.Length; i++)
        {
            if (!string.Equals(key.GetValue($"Axorith_{i}") as string, BlockedExecutables[i],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void NotifyPolicyChanged()
    {
        _ = SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "Policy",
            0x0002, 2000, out _);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}
