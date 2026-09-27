using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Axorith.Module.SiteBlocker;
using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class SiteBlockerAllowListTests
{
    [Fact]
    public async Task EmptyAllowListIsValidatedAndSentToBrowserExtensions()
    {
        using var blocker = new RecordingProcessBlocker();
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new TestNotifier(), blocker);
        ((Setting<string>)module.GetSettings().Single(setting => setting.Key == "Mode")).SetValue("AllowList");
        ((Setting<List<string>>)module.GetSettings().Single(setting => setting.Key == "Categories")).SetValue([]);
        ((Setting<string>)module.GetSettings().Single(setting => setting.Key == "CustomSites")).SetValue(string.Empty);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeRequests = ServeChromeExtensionAsync(timeout.Token);

        var validation = await module.ValidateSettingsAsync(timeout.Token);
        Assert.Equal(ValidationStatus.Ok, validation.Status);
        Assert.True(await module.CanStartCommittedSessionAsync(timeout.Token));
        await module.OnSessionStartAsync(timeout.Token);
        Assert.True(await module.IsProtectionHealthyAsync(timeout.Token));
        await module.OnSessionEndAsync(timeout.Token);
        timeout.Cancel();

        var requests = await pipeRequests;
        var blockRequests = requests.Where(request => request.Command == "block").ToArray();
        Assert.NotEmpty(blockRequests);
        Assert.All(blockRequests, request =>
        {
            Assert.Equal("AllowList", request.Mode);
            Assert.Empty(request.Sites);
        });
        Assert.Contains(requests, request => request.Command == "health");
        Assert.Contains(requests, request => request.Command == "unblock");
        Assert.Contains(blocker.BlockedProcesses, name => name.Contains("firefox", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<List<ExtensionRequest>> ServeChromeExtensionAsync(CancellationToken cancellationToken)
    {
        var requests = new List<ExtensionRequest>();
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream("axorith-nm-pipe-chrome", PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            var requestText = await reader.ReadLineAsync(cancellationToken);
            if (requestText == null)
            {
                continue;
            }

            using var document = JsonDocument.Parse(requestText);
            var root = document.RootElement;
            var command = root.GetProperty("command").GetString()!;
            var request = new ExtensionRequest(command,
                root.TryGetProperty("mode", out var mode) ? mode.GetString() : null,
                root.TryGetProperty("sites", out var sites)
                    ? sites.EnumerateArray().Select(site => site.GetString()!).ToArray()
                    : []);
            requests.Add(request);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                requestId = root.GetProperty("requestId").GetString(),
                protocolVersion = 1,
                status = "Connected",
                browser = "chromium",
                ok = true,
                blocking = command == "block",
                version = "test"
            }));
        }

        return requests;
    }

    private sealed record ExtensionRequest(string Command, string? Mode, string[] Sites);

    private sealed class TestModuleLogger : IModuleLogger
    {
        public void LogDebug(string messageTemplate, params object[] args) { }
        public void LogInfo(string messageTemplate, params object[] args) { }
        public void LogWarning(string messageTemplate, params object[] args) { }
        public void LogError(Exception? exception, string messageTemplate, params object[] args) { }
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) { }
    }

    private sealed class TestNotifier : INotifier
    {
        public void ShowToast(string message, NotificationType type = NotificationType.Info) { }
        public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) => Task.CompletedTask;
    }

    private sealed class RecordingProcessBlocker : IProcessBlocker
    {
        public List<string> BlockedProcesses { get; } = [];
        public bool IsMonitoring { get; private set; }
        public event Action<string>? ProcessBlocked
        {
            add { }
            remove { }
        }

        public List<string> Block(IEnumerable<string> processNames)
        {
            BlockedProcesses.AddRange(processNames);
            IsMonitoring = true;
            return [];
        }

        public List<string> AllowOnly(IEnumerable<string> processNames) => [];
        public void Unblock(string processName) { }
        public void UnblockAll() => IsMonitoring = false;
        public void Dispose() => UnblockAll();
    }
}
