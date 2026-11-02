using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Shared.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed partial class HostProcessEndToEndTests
{
    [InstalledFirefoxTheory]
    [InlineData("BlockList", "blocked.axorith.test", "graceful")]
    [InlineData("BlockList", "blocked.axorith.test", "crash")]
    [InlineData("BlockList", "blocked.axorith.test", "repair")]
    [InlineData("AllowList", "allowed.axorith.test", "graceful")]
    [InlineData("AllowList", "allowed.axorith.test", "crash")]
    [InlineData("AllowList", "allowed.axorith.test", "repair")]
    public async Task InstalledFirefoxUsesItsNativeHostAndPreservesInputAcrossBlockingAndHostExit(
        string mode, string site, string hostExit)
    {
        using var data = new TemporaryDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var extension = Directory.CreateDirectory(Path.Combine(data.Path, "extension")).FullName;
        var source = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
        File.Copy(Path.Combine(source, "Firefox", "manifest.json"), Path.Combine(extension, "manifest.json"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "Common")))
            File.Copy(file, Path.Combine(extension, Path.GetFileName(file)));
        var archive = Path.Combine(data.Path, "site-blocker.xpi");
        ZipFile.CreateFromDirectory(extension, archive);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var pages = builder.Build();
        pages.MapGet("/", () => Results.Content("<title>Axorith E2E</title><input id='draft' value='original'>", "text/html"));
        await pages.StartAsync(deadline.Token);
        var pagePort = new Uri(Assert.Single(pages.Urls)).Port;
        await using var host = await HostProcess.StartAsync(data.Path);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var driverPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("AXORITH_E2E_GECKODRIVER_EXE")!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "--host", "127.0.0.1", "--port", driverPort.ToString() }) start.ArgumentList.Add(argument);
        start.Environment["AXORITH_ROAMING_ROOT"] = data.Path;
        start.Environment["AXORITH_LOCAL_ROOT"] = Path.Combine(data.Path, "local");
        start.Environment["TEMP"] = start.Environment["TMP"] = data.Path;
        using var driver = Process.Start(start)!;
        var driverOutput = driver.StandardOutput.ReadToEndAsync();
        var driverError = driver.StandardError.ReadToEndAsync();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{driverPort}/") };
        string? session = null;
        try
        {
            await Until(async () =>
            {
                if (driver.HasExited) throw new InvalidOperationException(await driverOutput + await driverError);
                try { return (await Call(HttpMethod.Get, "status")).GetProperty("ready").GetBoolean(); }
                catch (HttpRequestException) { return false; }
            });
            var created = await Call(HttpMethod.Post, "session", new
            {
                capabilities = new { alwaysMatch = new Dictionary<string, object?>
                {
                    ["browserName"] = "firefox",
                    ["moz:firefoxOptions"] = new
                    {
                        binary = Environment.GetEnvironmentVariable("AXORITH_E2E_FIREFOX_EXE"),
                        args = new[] { "-headless" },
                        prefs = new Dictionary<string, object?>
                        {
                            ["network.proxy.type"] = 0, ["network.trr.mode"] = 5,
                            ["devtools.console.stdout.chrome"] = true,
                            ["devtools.console.stdout.content"] = true, ["extensions.logging.enabled"] = true
                        }
                    }
                }}
            });
            session = created.GetProperty("sessionId").GetString()!;
            var prefix = "session/" + session + "/";
            var addon = await Call(HttpMethod.Post, prefix + "moz/addon/install", new { path = archive, temporary = true });
            Assert.Equal(SiteBlockerExtensionIds.Firefox, addon.GetString());
            var blocked = (await Call(HttpMethod.Get, prefix + "window")).GetString()!;
            await Call(HttpMethod.Post, prefix + "url", new { url = $"http://blocked.axorith.test:{pagePort}/" });
            var allowed = (await Call(HttpMethod.Post, prefix + "window/new", new { type = "tab" })).GetProperty("handle").GetString()!;
            await Call(HttpMethod.Post, prefix + "window", new { handle = allowed });
            await Call(HttpMethod.Post, prefix + "url", new { url = $"http://allowed.axorith.test:{pagePort}/" });
            Assert.True(await Evaluate(blocked, "(document.querySelector('#draft').value = 'unsaved text') === 'unsaved text'"));
            var module = Assert.Single(await host.Modules.ListModulesAsync(deadline.Token), item => item.Name == "Site Blocker");
            var preset = await host.Presets.CreatePresetAsync(new SessionPreset
            {
                Id = Guid.NewGuid(), Name = "Installed Firefox protection", Modules = [new ConfiguredModule
                {
                    ModuleId = module.Id, Settings = new Dictionary<string, string>
                    { ["Mode"] = mode, ["Categories"] = "[]", ["CustomSites"] = site }
                }]
            }, deadline.Token);
            var started = await host.Sessions.StartSessionAsync(preset.Id, Guid.NewGuid(), deadline.Token);
            Assert.True(started.Success, started.Message);
            await Until(() => HasOverlay(blocked));
            Assert.Contains("Firefox Connected", (await host.Sessions.GetCurrentSessionAsync(deadline.Token))!.ProtectionStatus);
            Assert.False(await HasOverlay(allowed));
            Assert.True(await Evaluate(blocked, "(() => { document.querySelector('#draft').focus(); return document.activeElement !== document.querySelector('#draft'); })()"));
            Assert.True(await Evaluate(allowed, "(() => { document.querySelector('#draft').focus(); return document.activeElement === document.querySelector('#draft'); })()"));
            Assert.True((await host.Sessions.StopSessionAsync(deadline.Token)).Success);
            await Until(async () => !await HasOverlay(blocked));
            Assert.True(await Evaluate(blocked, "document.querySelector('#draft').value === 'unsaved text'"));
            Assert.True((await host.Sessions.StartSessionAsync(preset.Id, Guid.NewGuid(), deadline.Token)).Success);
            await Until(() => HasOverlay(blocked));
            if (hostExit == "repair") await RepairInstalledProductAsync(deadline.Token);
            else if (hostExit == "crash") await host.CrashAsync();
            else await host.StopAsync();
            await Until(async () => !await HasOverlay(blocked));
            Assert.False(await HasOverlay(allowed));
            Assert.True(await Evaluate(blocked, "document.querySelector('#draft').value === 'unsaved text'"));

            async Task<bool> Evaluate(string window, string expression)
            {
                await Call(HttpMethod.Post, prefix + "window", new { handle = window });
                return (await Call(HttpMethod.Post, prefix + "execute/sync", new { script = "return " + expression, args = Array.Empty<object>() })).GetBoolean();
            }
            Task<bool> HasOverlay(string window) => Evaluate(window,
                "!!document.querySelector('dialog[aria-label=\"Site blocked by Axorith\"][open]')");
        }
        finally
        {
            try
            {
                if (session is not null)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Call(HttpMethod.Delete, "session/" + session, token: cleanup.Token);
                }
            }
            finally
            {
                if (!driver.HasExited) { driver.Kill(entireProcessTree: true); await driver.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                var log = Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName, "firefox-e2e.log");
                var shimLog = Path.Combine(data.Path, "logs", "shim_error.log");
                await File.AppendAllTextAsync(log, $"\n=== {mode}/{hostExit} ===\n" + await driverOutput + await driverError +
                    "\nShim errors:\n" + (File.Exists(shimLog) ? await File.ReadAllTextAsync(shimLog) : "none"));
            }
        }

        async Task<JsonElement> Call(HttpMethod method, string path, object? payload = null, CancellationToken? token = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            using var response = await http.SendAsync(request, token ?? deadline.Token);
            var body = await response.Content.ReadAsStringAsync(token ?? deadline.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"WebDriver {path}: {body}");
            using var result = JsonDocument.Parse(body);
            return result.RootElement.GetProperty("value").Clone();
        }
        async Task Until(Func<Task<bool>> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            while (!await condition()) await Task.Delay(100, timeout.Token);
        }
    }

    private sealed class InstalledFirefoxTheoryAttribute : TheoryAttribute
    {
        public InstalledFirefoxTheoryAttribute()
        {
            var disposable = Environment.GetEnvironmentVariable("USERNAME") == "WDAGUtilityAccount" ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted";
            if (!OperatingSystem.IsWindows() || !disposable ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_FIREFOX_EXE")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_GECKODRIVER_EXE")))
                Skip = "Requires installed Firefox and geckodriver in disposable Windows; run the installer E2E runner.";
        }
    }
}
