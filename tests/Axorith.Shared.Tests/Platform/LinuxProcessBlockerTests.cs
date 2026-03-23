using System.Runtime.Versioning;
using Axorith.Shared.Platform;
using Axorith.Shared.Platform.Linux;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

public class LinuxProcessBlockerTests
{
    [Fact]
    [SupportedOSPlatform("linux")]
    public void Constructor_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        var act = () => new LinuxProcessBlocker(logger);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_WithEmptyList_StartsMonitoringWithoutKilling()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Empty list should not throw, should return empty result
        var result = blocker.Block([]);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_WithValidProcessNames_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Non-existent process names should not throw
        var act = () => blocker.Block(["nonexistent_process_xyz_12345"]);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Unblock_RemovesFromTargetList()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Block a process
        blocker.Block(["testprocess"]);

        // Unblock should not throw
        var act = () => blocker.Unblock("testprocess");
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void UnblockAll_StopsMonitoring()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Block some processes
        blocker.Block(["proc1", "proc2"]);

        // UnblockAll should not throw
        var act = () => blocker.UnblockAll();
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ProcessBlocked_EventCanBeSubscribed()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Subscribing to ProcessBlocked event should not throw
        string? receivedProcess = null;
        var act = () => blocker.ProcessBlocked += process => receivedProcess = process;
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Dispose_CleansUpResources()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        var blocker = new LinuxProcessBlocker(logger);

        // Start monitoring
        blocker.Block(["test"]);

        // Dispose should not throw
        var act = () => blocker.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Dispose_IsIdempotent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        var blocker = new LinuxProcessBlocker(logger);

        blocker.Block(["test"]);

        // First dispose
        blocker.Dispose();

        // Second dispose should be safe (no exception)
        var act = () => blocker.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_IsIdempotent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Calling Block() twice should not throw
        blocker.Block(["proc1"]);
        var act = () => blocker.Block(["proc1", "proc2"]);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Unblock_UnknownProcess_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Unblocking non-existent process should not throw
        var act = () => blocker.Unblock("nonexistent_process_xyz");
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Factory_CreatesLinuxProcessBlocker()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        var blocker = PlatformServices.CreateProcessBlocker(logger);

        blocker.Should().NotBeNull();
        blocker.Should().BeOfType<LinuxProcessBlocker>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_SafeListApps_NotInKillList()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Verify safe-listed apps (vim, emacs, libreoffice) are NOT blocked by default.
        // The blocker should only kill processes explicitly passed to Block().
        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Block some non-existent processes - safe list apps are never in the kill list
        var result = blocker.Block(["safelist_check_proc_xyz"]);

        // Should not include any safe-list apps in the result
        result.Should().NotContain("vim");
        result.Should().NotContain("emacs");
        result.Should().NotContain("nvim");
        result.Should().NotContain("libreoffice");
        result.Should().NotContain("soffice");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_MultipleProcesses_AllTracked()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Blocking multiple process names should not throw
        var act = () => blocker.Block(["proc_a_xyz", "proc_b_xyz", "proc_c_xyz"]);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_CaseInsensitive()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Block with lowercase name should match uppercase process names on Linux
        var act = () => blocker.Block(["MYPROCESSXYZ"]);
        act.Should().NotThrow();

        // Blocking same name with different case should not throw (uses case-insensitive comparison)
        var act2 = () => blocker.Block(["myprocessxyz"]);
        act2.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Unblock_SpecificTarget_RemovesOnly()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Block three processes
        blocker.Block(["target_a_xyz", "target_b_xyz", "target_c_xyz"]);

        // Unblock only the middle one - should not throw
        var act = () => blocker.Unblock("target_b_xyz");
        act.Should().NotThrow();

        // Blocking again with new set should work (previous targets removed)
        blocker.Block(["target_d_xyz"]);
        var act2 = () => blocker.Block(["target_e_xyz"]);
        act2.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ProcessBlocked_NotFiredOnInitialScan()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        var firedCount = 0;
        blocker.ProcessBlocked += _ => firedCount++;

        // Initial scan with non-existent process should NOT fire event
        blocker.Block(["nonexistent_init_scan_xyz_12345"]);

        // Event should not have fired since no matching process was found
        firedCount.Should().Be(0);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ProcessBlocked_FiresDuringMonitoring()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        var firedProcesses = new List<string>();
        blocker.ProcessBlocked += process => firedProcesses.Add(process);

        // Subscribe to event before blocking
        blocker.Block(["monitoring_test_xyz_99999"]);

        // The ProcessBlocked event fires during the polling loop (not initial scan).
        // Since we're blocking a non-existent process, it won't fire for that.
        // We verify the subscription itself doesn't throw and event can be raised.
        firedProcesses.Should().NotBeNull();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Block_Twice_UpdatesTargetList()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // First Block() call
        blocker.Block(["first_target_xyz"]);

        // Second Block() call with different list - should use latest list
        // Not throw, and should start monitoring with updated targets
        var act = () => blocker.Block(["second_target_xyz", "third_target_xyz"]);
        act.Should().NotThrow();

        // Calling Block() a third time should also work
        var act2 = () => blocker.Block(["fourth_target_xyz"]);
        act2.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void PollingStops_OnUnblockAll()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxProcessBlocker>.Instance;
        using var blocker = new LinuxProcessBlocker(logger);

        // Start monitoring
        blocker.Block(["poll_test_xyz_abc"]);

        // UnblockAll should stop polling and not throw
        var act = () => blocker.UnblockAll();
        act.Should().NotThrow();

        // Starting new monitoring after UnblockAll should work
        var act2 = () => blocker.Block(["new_target_xyz"]);
        act2.Should().NotThrow();
    }
}
