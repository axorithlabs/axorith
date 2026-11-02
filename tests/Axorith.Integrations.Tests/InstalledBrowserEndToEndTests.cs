using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Axorith.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed partial class HostProcessEndToEndTests
{
    [InstalledBrowserTheory]
    [InlineData("Chrome", "BlockList", "blocked.axorith.test", "graceful")]
    [InlineData("Chrome", "BlockList", "blocked.axorith.test", "crash")]
    [InlineData("Chrome", "BlockList", "blocked.axorith.test", "repair")]
    [InlineData("Chrome", "AllowList", "allowed.axorith.test", "graceful")]
    [InlineData("Chrome", "AllowList", "allowed.axorith.test", "crash")]
    [InlineData("Chrome", "AllowList", "allowed.axorith.test", "repair")]
    [InlineData("Chrome", "BlockList", "blocked.axorith.test", "committed-recovery")]
    [InlineData("Edge", "BlockList", "blocked.axorith.test", "graceful")]
    [InlineData("Edge", "BlockList", "blocked.axorith.test", "crash")]
    [InlineData("Edge", "BlockList", "blocked.axorith.test", "repair")]
    [InlineData("Edge", "AllowList", "allowed.axorith.test", "graceful")]
    [InlineData("Edge", "AllowList", "allowed.axorith.test", "crash")]
    [InlineData("Edge", "AllowList", "allowed.axorith.test", "repair")]
    public async Task InstalledBrowserPreservesThePageAcrossBlockingStopHostExitAndInstallerRepair(
        string browserName, string mode, string site, string hostExit)
    {
        using var data = new TemporaryDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var extension = Directory.CreateDirectory(Path.Combine(data.Path, "extension")).FullName;
        var source = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
        File.Copy(Path.Combine(source, "Chrome", "manifest.json"), Path.Combine(extension, "manifest.json"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "Common")))
            File.Copy(file, Path.Combine(extension, Path.GetFileName(file)));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(extension, "manifest.json"), deadline.Token));
        var keyHash = SHA256.HashData(Convert.FromBase64String(manifest.RootElement.GetProperty("key").GetString()!));
        var extensionId = string.Concat(keyHash.Take(16).Select(value => $"{(char)('a' + (value >> 4))}{(char)('a' + (value & 15))}"));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var pages = builder.Build();
        pages.MapGet("/", () => Results.Content("<title>Axorith E2E</title><input id='draft' value='original'>", "text/html"));
        await pages.StartAsync(deadline.Token);
        var port = new Uri(Assert.Single(pages.Urls)).Port;
        // Chrome for Testing uses Chromium's native-host registry, rather than branded Chrome's.
        await using var host = await HostProcess.StartAsync(data.Path, extensionId, browserName == "Edge");
        var profile = Directory.CreateDirectory(Path.Combine(data.Path, "browser-profile")).FullName;
        var browserPath = Environment.GetEnvironmentVariable(browserName == "Edge" ? "AXORITH_E2E_EDGE_EXE" : "AXORITH_E2E_BROWSER_EXE");
        Assert.True(File.Exists(browserPath), $"The disposable {browserName} executable is unavailable.");
        var start = new ProcessStartInfo(browserPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[]
        {
            "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-sync",
            "--disable-background-networking", "--no-proxy-server", "--remote-debugging-port=0",
            "--host-resolver-rules=MAP *.axorith.test 127.0.0.1",
            "--user-data-dir=" + profile, "about:blank"
        }) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(browserName == "Edge" ? "--enable-unsafe-extension-debugging" : "--load-extension=" + extension);
        start.Environment["AXORITH_ROAMING_ROOT"] = data.Path;
        start.Environment["AXORITH_LOCAL_ROOT"] = Path.Combine(data.Path, "local");
        using var browser = Process.Start(start)!;
        var browserErrors = browser.StandardError.ReadToEndAsync();
        using var cdp = new BrowserProtocol();
        try
        {
            var activePort = Path.Combine(profile, "DevToolsActivePort");
            await Until(async () => File.Exists(activePort) && (await File.ReadAllLinesAsync(activePort, deadline.Token)).Length >= 2);
            var address = await File.ReadAllLinesAsync(activePort, deadline.Token);
            await cdp.ConnectAsync(new Uri($"ws://127.0.0.1:{address[0]}{address[1]}"), deadline.Token);
            if (browserName == "Edge")
            {
                var installed = await cdp.SendAsync("Extensions.loadUnpacked", new { path = extension }, deadline.Token);
                Assert.Equal(extensionId, installed.GetProperty("id").GetString());
            }
            JsonElement worker = default;
            await Until(async () =>
            {
                worker = (await cdp.SendAsync("Target.getTargets", new { }, deadline.Token))
                    .GetProperty("targetInfos").EnumerateArray().FirstOrDefault(target =>
                        target.GetProperty("url").GetString() == $"chrome-extension://{extensionId}/background.js");
                return worker.ValueKind != JsonValueKind.Undefined;
            });
            var attachedWorker = await cdp.SendAsync("Target.attachToTarget",
                new { targetId = worker.GetProperty("targetId").GetString(), flatten = true }, deadline.Token);
            await cdp.SendAsync("Runtime.enable", new { }, deadline.Token, attachedWorker.GetProperty("sessionId").GetString());

            var blocked = await OpenPage($"http://blocked.axorith.test:{port}/");
            var allowed = await OpenPage($"http://allowed.axorith.test:{port}/");
            Assert.True(await cdp.EvaluateAsync(blocked, "document.querySelector('#draft').focus(); (document.querySelector('#draft').value = 'unsaved text') === 'unsaved text'", deadline.Token));
            var siteBlocker = Assert.Single(await host.Modules.ListModulesAsync(deadline.Token), module => module.Name == "Site Blocker");
            var preset = await host.Presets.CreatePresetAsync(new SessionPreset
            {
                Id = Guid.NewGuid(), Name = "Installed browser protection",
                FocusCommitment = hostExit == "committed-recovery" ? new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked, EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromSeconds(90), BreakCount = 1, BreakDuration = TimeSpan.FromMinutes(1)
                } : new FocusCommitmentOptions(),
                Modules = [new ConfiguredModule
                {
                    ModuleId = siteBlocker.Id, Settings = new Dictionary<string, string>
                    { ["Mode"] = mode, ["Categories"] = "[]", ["CustomSites"] = site }
                }]
            }, deadline.Token);
            var started = await host.Sessions.StartSessionAsync(preset.Id, Guid.NewGuid(), deadline.Token);
            Assert.True(started.Success, started.Message);
            await Until(() => HasOverlay(blocked));
            Assert.Contains(browserName + " Connected", (await host.Sessions.GetCurrentSessionAsync(deadline.Token))!.ProtectionStatus);
            Assert.False(await HasOverlay(allowed));
            Assert.True(await cdp.EvaluateAsync(blocked, "document.querySelector('#draft').focus(); document.activeElement !== document.querySelector('#draft')", deadline.Token));
            Assert.True(await cdp.EvaluateAsync(allowed, "document.querySelector('#draft').focus(); document.activeElement === document.querySelector('#draft')", deadline.Token));
            Assert.True(await cdp.EvaluateAsync(blocked, "document.querySelector('#draft').value === 'unsaved text'", deadline.Token));

            if (hostExit == "committed-recovery")
            {
                var original = (await host.Sessions.GetCurrentSessionAsync(deadline.Token))!;
                Assert.NotNull(original.EndsAt);
                Assert.False((await host.Sessions.StopSessionAsync(deadline.Token)).Success);
                Assert.True((await host.Sessions.StartBreakAsync(deadline.Token)).Success);
                await Until(async () => !await HasOverlay(blocked));
                Assert.True(await cdp.EvaluateAsync(blocked, "document.activeElement === document.querySelector('#draft')", deadline.Token));
                await Until(() => HasOverlay(blocked), TimeSpan.FromSeconds(75));
                Assert.Equal(0, (await host.Sessions.GetCurrentSessionAsync(deadline.Token))!.BreaksRemaining);
                Assert.False((await host.Sessions.StartBreakAsync(deadline.Token)).Success);
                var eventIndex = cdp.Events.Count;
                await host.CrashAsync();
                await Until(async () =>
                {
                    Assert.True(await HasOverlay(blocked), "Committed browser rules must survive the native host reporting Host unavailable.");
                    return cdp.Events.Skip(eventIndex).Any(entry => entry.GetRawText().Contains("\"hostUnavailable\"", StringComparison.Ordinal));
                }, TimeSpan.FromSeconds(10));
                await using var recovered = await HostProcess.StartAsync(data.Path, extensionId);
                var state = (await recovered.Sessions.GetCurrentSessionAsync(deadline.Token))!;
                Assert.Equal(original.EndsAt, state.EndsAt);
                Assert.Equal(preset.Id, state.PresetId);
                Assert.Equal(0, state.BreaksRemaining);
                await Until(async () => await recovered.Sessions.GetCurrentSessionAsync(deadline.Token) is null,
                    TimeSpan.FromSeconds(45));
                await Until(async () => !await HasOverlay(blocked));
                Assert.False(await HasOverlay(allowed));
                Assert.True(await cdp.EvaluateAsync(blocked, "document.querySelector('#draft').value === 'unsaved text'", deadline.Token));
                Assert.Contains(await recovered.Sessions.GetSessionHistoryAsync(deadline.Token), entry => entry.PresetName == preset.Name);
                await recovered.StopAsync();
                return;
            }

            var stopped = await host.Sessions.StopSessionAsync(deadline.Token);
            Assert.True(stopped.Success, stopped.Message);
            await Until(async () => !await HasOverlay(blocked));
            Assert.True(await cdp.EvaluateAsync(blocked, "document.querySelector('#draft').value === 'unsaved text'", deadline.Token));
            Assert.True(await cdp.EvaluateAsync(blocked, "document.activeElement === document.querySelector('#draft')", deadline.Token));
            Assert.True((await host.Sessions.StartSessionAsync(preset.Id, Guid.NewGuid(), deadline.Token)).Success);
            await Until(() => HasOverlay(blocked));
            if (hostExit == "repair")
            {
                await RepairInstalledProductAsync(deadline.Token);
            }
            else if (hostExit == "crash") await host.CrashAsync();
            else await host.StopAsync();
            await Until(async () => !await HasOverlay(blocked));
            Assert.False(await HasOverlay(allowed));
            Assert.True(await cdp.EvaluateAsync(blocked, "document.querySelector('#draft').value === 'unsaved text'", deadline.Token));

            Task<bool> HasOverlay(string session) => cdp.EvaluateAsync(session,
                "!!document.querySelector('dialog[aria-label=\"Site blocked by Axorith\"][open]')", deadline.Token);
            async Task<string> OpenPage(string url)
            {
                var target = await cdp.SendAsync("Target.createTarget", new { url }, deadline.Token);
                var attached = await cdp.SendAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true }, deadline.Token);
                var session = attached.GetProperty("sessionId").GetString()!;
                await Until(() => cdp.EvaluateAsync(session, "document.readyState === 'complete' && document.title === 'Axorith E2E' && !!document.querySelector('#draft')", deadline.Token));
                return session;
            }
        }
        finally
        {
            if (!browser.HasExited) { browser.Kill(entireProcessTree: true); await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            var log = Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName, "browser-e2e.log");
            var shimLog = Path.Combine(data.Path, "logs", "shim_error.log");
            await File.WriteAllTextAsync(log, await browserErrors + "\nExtension console:\n" +
                JsonSerializer.Serialize(cdp.Events) + "\nShim errors:\n" +
                (File.Exists(shimLog) ? await File.ReadAllTextAsync(shimLog) : "none"));
        }

        async Task Until(Func<Task<bool>> condition, TimeSpan? wait = null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            timeout.CancelAfter(wait ?? TimeSpan.FromSeconds(25));
            while (!await condition()) await Task.Delay(100, timeout.Token);
        }
    }

    private static async Task RepairInstalledProductAsync(CancellationToken cancellationToken)
    {
        var buildRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var installerPath = Path.Combine(Directory.GetParent(buildRoot)!.FullName, "Installer", "axorith-setup.exe");
        var setup = new ProcessStartInfo(installerPath) { UseShellExecute = false, CreateNoWindow = true };
        setup.ArgumentList.Add("/S");
        setup.Environment["AXORITH_TEST_MODE"] = "0";
        setup.Environment["AXORITH_DEV"] = "1";
        setup.Environment["AXORITH_TELEMETRY_DISABLED"] = "1";
        using var maintenance = Process.Start(setup)!;
        try
        {
            await maintenance.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(45));
            Assert.Equal(0, maintenance.ExitCode);
        }
        finally
        {
            if (!maintenance.HasExited)
            {
                maintenance.Kill(entireProcessTree: true);
                await maintenance.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private sealed class InstalledBrowserTheoryAttribute : TheoryAttribute
    {
        public InstalledBrowserTheoryAttribute()
        {
            var disposable = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
                             Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted" ||
                             Environment.GetEnvironmentVariable("USERNAME") == "WDAGUtilityAccount";
            if (!OperatingSystem.IsWindows() || !disposable ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_BROWSER_EXE")))
                Skip = "Requires the installed product and Chrome for Testing in disposable Windows; run scripts/test-installer.ps1.";
        }
    }

    private sealed class BrowserProtocol : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private int _nextId;
        public List<JsonElement> Events { get; } = [];
        public Task ConnectAsync(Uri uri, CancellationToken ct) => _socket.ConnectAsync(uri, ct);

        public async Task<JsonElement> SendAsync(string method, object arguments, CancellationToken ct, string? session = null)
        {
            var id = ++_nextId;
            var message = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = arguments };
            if (session is not null) message["sessionId"] = session;
            await _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message).AsMemory(), WebSocketMessageType.Text, true, ct);
            var buffer = new byte[16 * 1024];
            while (true)
            {
                using var body = new MemoryStream();
                ValueWebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(buffer.AsMemory(), ct);
                    if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Browser closed its DevTools connection.");
                    body.Write(buffer, 0, received.Count);
                    if (body.Length > 1024 * 1024) throw new InvalidDataException("Unexpectedly large DevTools response.");
                } while (!received.EndOfMessage);
                using var response = JsonDocument.Parse(body.ToArray());
                var root = response.RootElement;
                if (!root.TryGetProperty("id", out var responseId)) { Events.Add(root.Clone()); continue; }
                Assert.Equal(id, responseId.GetInt32());
                if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetRawText());
                return root.GetProperty("result").Clone();
            }
        }

        public async Task<bool> EvaluateAsync(string session, string expression, CancellationToken ct)
        {
            var response = await SendAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true }, ct, session);
            if (response.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.GetRawText());
            return response.GetProperty("result").GetProperty("value").GetBoolean();
        }

        public void Dispose() => _socket.Dispose();
    }
}
