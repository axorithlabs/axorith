using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Axorith.Client.Services;

internal static class ChromeExtensionDownload
{
    internal const string DownloadUrl =
        "https://github.com/axorithlabs/axorith/releases/latest/download/chrome-extension-latest.zip";

    internal static async Task<string> DownloadAsync(HttpClient client, string directory)
    {
        using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("The latest GitHub release does not contain chrome-extension-latest.zip yet.");
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(10 * 1024 * 1024);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using (var archive = new ZipArchive(new MemoryStream(bytes)))
        {
            var manifest = archive.GetEntry("manifest.json")
                ?? throw new InvalidDataException("The downloaded ZIP has no manifest.json at its root.");
            using var manifestStream = manifest.Open();
            using var document = await JsonDocument.ParseAsync(manifestStream);
            if (!document.RootElement.TryGetProperty("manifest_version", out var version) ||
                !version.TryGetInt32(out var number) || number != 3)
                throw new InvalidDataException("The downloaded ZIP is not a Chrome Manifest V3 extension.");
        }

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"chrome-extension-{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }
}
