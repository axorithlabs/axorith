using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Axorith.Host.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class StrictWindowsPolicyEndToEndTests
{
    [DisposableSystemFact]
    [SupportedOSPlatform("windows")]
    public async Task RecoveryStartupPreflightAcceptsItsOwnEntryAndPreservesForeignEntries()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        const string name = "Axorith.CommittedSessionRecovery";
        var original = key.GetValue(name);
        var kind = original is null ? RegistryValueKind.String : key.GetValueKind(name);
        var service = new WindowsCommitmentProtectionService(Path.GetTempPath(),
            NullLogger<WindowsCommitmentProtectionService>.Instance);
        var own = $"\"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Axorith.Host.exe"))}\"";
        try
        {
            key.SetValue(name, own);
            await service.CheckCanSetRecoveryStartupAsync();
            await service.SetRecoveryStartupAsync(true);
            Assert.Equal(own, key.GetValue(name));
            await service.SetRecoveryStartupAsync(false);
            Assert.Null(key.GetValue(name));

            key.SetValue(name, "user-owned startup command");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckCanSetRecoveryStartupAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetRecoveryStartupAsync(true));
            await service.SetRecoveryStartupAsync(false);
            Assert.Equal("user-owned startup command", key.GetValue(name));
        }
        finally
        {
            if (original is null) key.DeleteValue(name, throwOnMissingValue: false);
            else key.SetValue(name, original, kind);
        }
    }

    [DisposableSystemFact]
    [SupportedOSPlatform("windows")]
    public async Task RealWindowsPoliciesAndProcessBlockingRestoreANonDefaultPolicyWithoutDeletingUnrelatedValues()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-real-policy-");
        const string policyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        using var policy = Registry.CurrentUser.CreateSubKey(policyPath, writable: true);
        var original = policy.GetValue("DisableTaskMgr");
        var originalKind = original is null ? RegistryValueKind.DWord : policy.GetValueKind("DisableTaskMgr");
        var otherPolicies = new[]
        {
            (Path: @"Software\Policies\Microsoft\Windows\System", Name: "DisableCMD", Enabled: 2),
            (Path: @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", Name: "NoRun", Enabled: 1),
            (Path: @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", Name: "DisallowRun", Enabled: 1)
        };
        var previous = otherPolicies.Select(item => ReadPolicy(item.Path, item.Name)).ToArray();
        policy.SetValue("DisableTaskMgr", 7, RegistryValueKind.DWord);
        var service = new WindowsCommitmentProtectionService(directory.FullName,
            NullLogger<WindowsCommitmentProtectionService>.Instance);
        var executable = Path.Combine(directory.FullName, "cmd.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);
        Process? probe = null;
        try
        {
            await service.CheckCanEnableAsync();
            await service.EnableAsync();
            Assert.True(await service.IsEnabledAsync());
            Assert.Equal(1, policy.GetValue("DisableTaskMgr"));
            foreach (var item in otherPolicies) Assert.Equal(item.Enabled, ReadPolicy(item.Path, item.Name));
            await new WindowsCommitmentProtectionService(directory.FullName,
                NullLogger<WindowsCommitmentProtectionService>.Instance).CheckRecoveryStateAsync();
            using (var restrictions = Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", writable: true))
            {
                Assert.NotNull(restrictions);
                Assert.Contains(restrictions.GetValueNames(), name => Equals(restrictions.GetValue(name), "WindowsTerminal.exe"));
                restrictions.SetValue("AxorithE2EUnrelated", "user-owned.exe", RegistryValueKind.String);
            }
            probe = StartProbe();
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(probe.HasExited, "The real Windows blocker did not terminate the owned cmd-named probe.");
            probe.Dispose();
            probe = null;

            await service.RestoreAsync();
            Assert.False(await new WindowsCommitmentProtectionService(directory.FullName,
                NullLogger<WindowsCommitmentProtectionService>.Instance).IsEnabledAsync());
            Assert.Equal(7, policy.GetValue("DisableTaskMgr"));
            for (var index = 0; index < otherPolicies.Length; index++)
                Assert.Equal(previous[index], ReadPolicy(otherPolicies[index].Path, otherPolicies[index].Name));
            using (var restrictions = Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", writable: true))
            {
                Assert.NotNull(restrictions);
                Assert.Equal("user-owned.exe", restrictions.GetValue("AxorithE2EUnrelated"));
                restrictions.DeleteValue("AxorithE2EUnrelated");
            }
            Assert.False(File.Exists(Path.Combine(directory.FullName, "strict-protection.json")));
            probe = StartProbe();
            await Task.Delay(1200);
            Assert.False(probe.HasExited, "Restoring policies must actually stop process blocking.");
        }
        finally
        {
            try { await service.RestoreAsync(); }
            finally
            {
                if (probe is not null)
                {
                    if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    probe.Dispose();
                }
                if (original is null) policy.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
                else policy.SetValue("DisableTaskMgr", original, originalKind);
                using var restrictions = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\DisallowRun", writable: true);
                restrictions?.DeleteValue("AxorithE2EUnrelated", throwOnMissingValue: false);
                directory.Delete(recursive: true);
            }
        }

        Process StartProbe() => Process.Start(new ProcessStartInfo(executable, "-t 127.0.0.1")
            { UseShellExecute = false, CreateNoWindow = true })!;
        static object? ReadPolicy(string path, string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name);
        }
    }

    private sealed class DisposableSystemFactAttribute : FactAttribute
    {
        public DisposableSystemFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("AXORITH_E2E_SYSTEM_POLICIES") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")))
            {
                Skip = "Requires a separate SYSTEM test process in the disposable installer runner.";
                return;
            }
            using var identity = WindowsIdentity.GetCurrent();
            if (!identity.IsSystem) Skip = "The isolated policy case must run under the guest SYSTEM account.";
        }
    }
}
