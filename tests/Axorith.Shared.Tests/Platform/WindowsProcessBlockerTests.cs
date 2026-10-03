using System.Reflection;
using Axorith.Shared.Platform;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

public sealed class WindowsProcessBlockerTests
{
    [Fact]
    public void ExplicitTaskManagerBlockIsNotSuppressedByTheSafeList()
    {
        var blockerType = typeof(IProcessBlocker).Assembly.GetType(
            "Axorith.Shared.Platform.Windows.WindowsProcessBlocker", throwOnError: true)!;
        var blocker = (IDisposable)Activator.CreateInstance(blockerType, NullLogger.Instance)!;
        try
        {
            blockerType.GetField("_targetProcessNames", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(blocker, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "taskmgr" });

            var target = blockerType.GetMethod("FindBlockedTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(blocker, ["taskmgr", null]);

            target.Should().Be("taskmgr");
        }
        finally
        {
            blocker.Dispose();
        }
    }
}
