using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Xunit;

namespace Axorith.Integrations.Tests;

[CollectionDefinition("SiteBlocker extension pipes", DisableParallelization = true)]
public sealed class SiteBlockerExtensionPipeCollection
{
}

[Collection("SiteBlocker extension pipes")]
public sealed class SiteBlockerAllowListTests
{
    [Fact]
    public void FirefoxInstallActionIsAvailableAndChromeInstallIsNotOffered()
    {
        using var blocker = new RecordingProcessBlocker();
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier(), blocker);
        var actions = module.GetActions();

        try
        {
            Assert.Contains(actions, action => action.Key == "InstallExtension.Firefox");
            Assert.DoesNotContain(actions, action => action.Key == "InstallExtension.Chrome");
        }
        finally
        {
            foreach (var action in actions.OfType<IDisposable>())
                action.Dispose();
        }
    }

    [Fact]
    public void CategorySettingLoadsChoicesFromModuleData()
    {
        using var blocker = new RecordingProcessBlocker();
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier(), blocker);

        var categories = module.GetSettings().Single(setting => setting.Key == "Categories");
        var choices = categories.GetCurrentChoices();

        Assert.NotNull(choices);
        Assert.Equal(12, choices.Count);
        Assert.Contains(choices, choice => choice.Key == "Social");
        Assert.Contains(choices, choice => choice.Key == "Forums");
    }

    [Fact]
    public async Task MissingBrowserExtensionConnectionProducesAWarning()
    {
        using var blocker = new RecordingProcessBlocker();
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier(), blocker);

        var validation = await module.ValidateSettingsAsync(CancellationToken.None);

        Assert.Equal(ValidationStatus.Warning, validation.Status);
        Assert.Contains("extension", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyAllowListIsValidatedAndSentToBrowserExtensions()
    {
        using var blocker = new RecordingProcessBlocker();
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier(), blocker);
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
        Assert.DoesNotContain(blocker.BlockedProcesses, name => name.Contains("firefox", StringComparison.OrdinalIgnoreCase));
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
