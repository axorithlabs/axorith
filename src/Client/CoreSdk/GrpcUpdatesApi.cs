using System.Diagnostics;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.CoreSdk;

public class GrpcUpdatesApi : IUpdatesApi
{
    private readonly UpdatesService.UpdatesServiceClient _client;
    private readonly ILogger<GrpcUpdatesApi> _logger;
    private readonly HttpClient _httpClient;

    public GrpcUpdatesApi(UpdatesService.UpdatesServiceClient client, ILogger<GrpcUpdatesApi> logger)
    {
        _client = client;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Axorith-Client");
    }

    public async Task<UpdateInfoDto?> GetUpdateInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetUpdateInfoAsync(new Empty(), cancellationToken: ct);

            if (!response.UpdateAvailable)
            {
                return null;
            }

            return new UpdateInfoDto(
                response.LatestVersion,
                response.DownloadUrl,
                response.ReleaseNotes,
                response.PublishedAt.ToDateTime());
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to get update info");
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfoDto updateInfo, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"Axorith-Setup-{updateInfo.Version}.exe");

            _logger.LogInformation("Downloading update from {Url} to {Path}", updateInfo.DownloadUrl, tempPath);

            using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percentage = (double)downloadedBytes / totalBytes * 100;
                    progress?.Report(percentage);
                }
            }

            _logger.LogInformation("Update downloaded successfully to {Path}", tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            throw;
        }
    }

    public Task InstallUpdateAsync(string installerPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException("Installer not found", installerPath);
            }

            _logger.LogInformation("Starting installer: {Path}", installerPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);

            Task.Run(async () =>
            {
                await Task.Delay(2000);
                Environment.Exit(0);
            });

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install update");
            throw;
        }
    }
}
