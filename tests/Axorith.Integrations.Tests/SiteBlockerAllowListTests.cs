using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
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
    public async Task DisposeWaitsForTheBrowserUnblockCommand()
    {
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier());
        module.GetSettings().Single(setting => setting.Key == "Mode").SetValueFromString("AllowList");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = ServeChromeExtensionAsync(timeout.Token);
        try
        {
            await module.OnSessionStartAsync(timeout.Token);
            module.Dispose();
        }
        finally
        {
            timeout.Cancel();
        }

        var requests = await server;
        Assert.Contains(requests, request => request.Command == "block");
        Assert.Equal("unblock", requests.Last().Command);
    }

    [Fact]
    public void BrowserInstallActionsAreAvailableInModuleSettings()
    {
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier());
        var actions = module.GetActions();

        try
        {
            Assert.Contains(actions, action => action.Key == "InstallExtension.Firefox");
            Assert.Contains(actions, action => action.Key == "InstallExtension.Chrome");
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
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier());

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
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier());

        var validation = await module.ValidateSettingsAsync(CancellationToken.None);

        Assert.Equal(ValidationStatus.Warning, validation.Status);
        Assert.Contains("extension", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyAllowListIsValidatedAndSentToBrowserExtensions()
    {
        using var module = new Axorith.Module.SiteBlocker.Module(new TestModuleLogger(), new NoopNotifier());
        ((Setting<string>)module.GetSettings().Single(setting => setting.Key == "Mode")).SetValue("AllowList");
        ((Setting<List<string>>)module.GetSettings().Single(setting => setting.Key == "Categories")).SetValue([]);
        ((Setting<string>)module.GetSettings().Single(setting => setting.Key == "CustomSites")).SetValue(string.Empty);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeRequests = ServeChromeExtensionAsync(timeout.Token);

        List<ExtensionRequest> requests;
        try
        {
            var validation = await module.ValidateSettingsAsync(timeout.Token);
            Assert.Equal(ValidationStatus.Warning, validation.Status);
            Assert.Contains("Allow List mode will block every website", validation.Message);
            Assert.True(await module.CanStartCommittedSessionAsync(timeout.Token), module.ProtectionStatusMessage);
            await module.OnSessionStartAsync(timeout.Token);
            for (var check = 0; check < 3; check++)
                Assert.True(await module.IsProtectionHealthyAsync(timeout.Token), module.ProtectionStatusMessage);
        }
        finally
        {
            try
            {
                await module.OnSessionEndAsync(CancellationToken.None);
            }
            finally
            {
                timeout.Cancel();
                requests = await pipeRequests;
            }
        }

        var blockRequests = requests.Where(request => request.Command == "block").ToArray();
        Assert.NotEmpty(blockRequests);
        Assert.All(blockRequests, request =>
        {
            Assert.Equal("AllowList", request.Mode);
            Assert.Empty(request.Sites);
        });
        Assert.Contains(requests, request => request.Command == "health");
        Assert.Contains(requests, request => request.Command == "unblock");
    }

    private static async Task<List<ExtensionRequest>> ServeChromeExtensionAsync(CancellationToken cancellationToken)
    {
        var requests = new List<ExtensionRequest>();
        var listeningPipe = CreateChromeExtensionPipe();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var pipe = listeningPipe;
                await pipe.WaitForConnectionAsync(cancellationToken);

                // Keep a listener available before replying so the next request cannot race pipe recreation.
                listeningPipe = CreateChromeExtensionPipe();

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
                }).AsMemory(), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await listeningPipe.DisposeAsync();
        }

        return requests;
    }

    private static NamedPipeServerStream CreateChromeExtensionPipe() =>
        new("axorith-nm-pipe-chrome", PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private sealed record ExtensionRequest(string Command, string? Mode, string[] Sites);
}
