using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using Axorith.Client.Services;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class BrowserExtensionPackagingTests
{
    [Theory]
    [InlineData("Chrome", "chrome-extension-latest.zip")]
    [InlineData("Firefox", "site-blocker-extension-firefox.zip")]
    public async Task PackagedExtensionContainsItsFilesAndCanBeDownloaded(string browser, string archiveName)
    {
        var extensionDirectory = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
        var browserDirectory = Path.Combine(extensionDirectory, browser);
        var archivePath = Path.Combine(browserDirectory, archiveName);
        File.Delete(archivePath);

        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                     Path.Combine(extensionDirectory, "package.ps1"), "-Browser", browser
                 })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(process.ExitCode == 0, await output + await error);
        Assert.True(File.Exists(archivePath), $"The packager did not produce {archiveName}.");
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("background.js"));
            Assert.NotNull(archive.GetEntry("icon.png"));
        }

        if (browser != "Chrome") return;

        var downloads = Directory.CreateTempSubdirectory("AxorithExtensionDownload-");
        try
        {
            using var client = new HttpClient(new ReleaseAssetsHandler(browserDirectory));
            var downloaded = await ChromeExtensionDownload.DownloadAsync(client, downloads.FullName);

            Assert.Equal(await File.ReadAllBytesAsync(archivePath), await File.ReadAllBytesAsync(downloaded));
        }
        finally
        {
            downloads.Delete(recursive: true);
        }
    }

    private sealed class ReleaseAssetsHandler(string directory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = Path.Combine(directory, Path.GetFileName(request.RequestUri!.AbsolutePath));
            return Task.FromResult(File.Exists(path)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
