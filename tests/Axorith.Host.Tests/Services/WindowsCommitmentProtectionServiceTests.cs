using System.Reflection;
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
}
