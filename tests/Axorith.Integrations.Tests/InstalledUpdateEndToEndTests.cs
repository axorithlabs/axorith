using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Shared.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class InstalledUpdateEndToEndTests
{
    [DisposableUpdateFact]
    [SupportedOSPlatform("windows")]
    public async Task ClientUpdateApiRunsTheRealWizardAndReplacesTheOlderInstalledVersion()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var root = Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")!;
        var host = Path.Combine(root, "Axorith.Host", "Axorith.Host.dll");
        var before = VersionOf(Path.Combine(
            Environment.GetEnvironmentVariable("AXORITH_E2E_PREVIOUS_INSTALL_ROOT")!, "Axorith.Host", "Axorith.Host.dll"));
        var build = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var installer = Path.Combine(Directory.GetParent(build)!.FullName, "Installer", "axorith-setup.exe");
        var version = VersionOf(installer);
        Assert.True(Version.Parse(before) < Version.Parse(version));
        using var endpoint = new InstallerResponse(installer);
        using var http = new HttpClient(endpoint);
        using var api = new GrpcUpdatesApi(null!, NullLogger<GrpcUpdatesApi>.Instance, http);
        using var input = File.OpenRead(installer);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token));
        var update = new UpdateInfoDto(version, UpdateInstallerSource.GetDownloadUri(version).AbsoluteUri,
            "Local release artifact", DateTime.UtcNow, hash);
        var path = await api.DownloadUpdateAsync(update, ct: deadline.Token);
        Assert.Equal(update.DownloadUrl, Assert.Single(endpoint.Requests));
        using var downloaded = File.OpenRead(path);
        Assert.Equal(hash, Convert.ToHexString(await SHA256.HashDataAsync(downloaded, deadline.Token)));
        downloaded.Close();
        var installation = Task.Run(() => api.InstallUpdateAsync(path, deadline.Token));
        Process? owned = null;
        try
        {
            while (!installation.IsCompleted)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (owned is null)
                {
                    foreach (var candidate in Process.GetProcessesByName("axorith-setup"))
                    {
                        try
                        {
                            if (string.Equals(candidate.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                            {
                                owned = candidate;
                                // Retain the OS handle so exit status remains available after the wizard closes.
                                _ = owned.SafeHandle;
                            }
                        }
                        catch (InvalidOperationException) { }
                        finally { if (candidate != owned) candidate.Dispose(); }
                    }
                }
                if (owned is { HasExited: false })
                {
                    owned.Refresh();
                    var next = GetDlgItem(owned.MainWindowHandle, 1);
                    if (next != IntPtr.Zero && IsWindowEnabled(next))
                        Assert.True(PostMessage(next, 0x00F5 /* BM_CLICK */, IntPtr.Zero, IntPtr.Zero));
                }
                await Task.Delay(200, deadline.Token);
            }
            await installation;
            Assert.NotNull(owned);
            Assert.Equal(0, owned.ExitCode);
            Assert.Equal(version, VersionOf(host));
            Assert.Equal(version, VersionOf(Path.Combine(root, "Axorith.Client", "Axorith.Client.dll")));
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        }
        finally
        {
            await deadline.CancelAsync();
            if (owned is not null)
            {
                if (!owned.HasExited) { owned.Kill(entireProcessTree: true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                owned.Dispose();
            }
            try { await installation; } catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }

        static string VersionOf(string file) => FileVersionInfo.GetVersionInfo(file).ProductVersion!.Split('+')[0];
    }

    private sealed class InstallerResponse(string installer) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(installer)) });
        }
    }

    private sealed class DisposableUpdateFactAttribute : FactAttribute
    {
        public DisposableUpdateFactAttribute()
        {
            var disposable = Environment.GetEnvironmentVariable("USERNAME") == "WDAGUtilityAccount" ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted";
            if (!OperatingSystem.IsWindows() || !disposable || Environment.GetEnvironmentVariable("AXORITH_E2E_UPDATE") != "1" ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")))
                Skip = "Requires the older installed product in the disposable installer upgrade phase.";
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
