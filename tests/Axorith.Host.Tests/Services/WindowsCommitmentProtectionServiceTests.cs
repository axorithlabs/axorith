using System.Reflection;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Axorith.Core.Services;
using Axorith.Host.Services;
using Axorith.Shared.Platform;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Host.Tests.Services;

public sealed class WindowsCommitmentProtectionServiceTests
{
    [Fact]
    public void StrictProtectionBlocksWindowsTerminalAndItsLaunchAlias()
    {
        var executableNames = (string[])typeof(WindowsCommitmentProtectionService)
            .GetField("BlockedExecutables", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var targetNames = (string[])typeof(WindowsCommitmentProtectionService)
            .GetField("BlockedProcessNames", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var blockerType = typeof(IProcessBlocker).Assembly.GetType(
            "Axorith.Shared.Platform.Windows.WindowsProcessBlocker", throwOnError: true)!;
        var blocker = (IDisposable)Activator.CreateInstance(blockerType, NullLogger.Instance)!;

        try
        {
            executableNames.Should().Contain("WindowsTerminal.exe");
            executableNames.Should().Contain(["msiexec.exe", "winget.exe", "RevoUnin.exe", "IObitUninstaller.exe",
                "Geek.exe", "BCUninstaller.exe", "UninstallTool.exe"]);
            blockerType.GetField("_targetProcessNames", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(blocker, new HashSet<string>(targetNames, StringComparer.OrdinalIgnoreCase));
            var findBlockedTarget = blockerType.GetMethod("FindBlockedTarget",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            findBlockedTarget.Invoke(blocker, ["wt", null]).Should().Be("wt");
            findBlockedTarget.Invoke(blocker, ["WindowsTerminal", null]).Should().Be("WindowsTerminal");
        }
        finally
        {
            blocker.Dispose();
        }
    }

    [Fact]
    public void RecoveryStartupEntryCollisionIsPreservedAndCleanupOnlyTargetsTheHostEntry()
    {
        var type = typeof(WindowsCommitmentProtectionService);
        var entryInUse = type.GetMethod("IsRecoveryStartupEntryInUse",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var isHostEntry = type.GetMethod("IsRecoveryStartupCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var hostCommand = $"\"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Axorith.Host.exe"))}\"";

        entryInUse.Invoke(null, [null]).Should().Be(false);
        entryInUse.Invoke(null, ["user-owned startup command"]).Should().Be(true);
        isHostEntry.Invoke(null, [hostCommand]).Should().Be(true);
        isHostEntry.Invoke(null, ["user-owned startup command"]).Should().Be(false);
    }

    [Fact]
    public async Task StrictRecoveryFailsClosedWhenItsPolicySnapshotIsMissingOrTampered()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-strict-state-test-");
        var statePath = Path.Combine(directory.FullName, "strict-protection.json");
        var service = new WindowsCommitmentProtectionService(directory.FullName,
            NullLogger<WindowsCommitmentProtectionService>.Instance);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckRecoveryStateAsync());
            File.Exists(statePath).Should().BeFalse();

            CommittedSessionStateFile.WritePayload(statePath, "{\"Values\":[]}");
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(statePath))!.AsObject();
            envelope["Payload"] = envelope["Payload"]!.GetValue<string>() + " ";
            await File.WriteAllTextAsync(statePath, envelope.ToJsonString());

            await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckRecoveryStateAsync());
            File.Exists(statePath).Should().BeTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StrictPreflightRejectsAnUnelevatedHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("axorith-strict-elevation-test-");
        try
        {
            var service = new WindowsCommitmentProtectionService(directory.FullName,
                NullLogger<WindowsCommitmentProtectionService>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckCanEnableAsync());
            error.Message.Should().Contain("run as administrator");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
