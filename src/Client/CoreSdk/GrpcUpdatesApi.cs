using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.CoreSdk;

public sealed class GrpcUpdatesApi : IUpdatesApi, IDisposable
{
    private readonly UpdatesService.UpdatesServiceClient _client;
    private readonly ILogger<GrpcUpdatesApi> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, string> _verifiedInstallers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(30);

    public GrpcUpdatesApi(UpdatesService.UpdatesServiceClient client, ILogger<GrpcUpdatesApi> logger, HttpClient? httpClient = null)
    {
        _client = client;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = DownloadIdleTimeout };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Axorith-Client");
    }

    public async Task<UpdateInfoDto?> GetUpdateInfoAsync(CancellationToken ct = default, bool forceCheck = false)
    {
        try
        {
            var response = forceCheck
                ? await _client.CheckForUpdatesAsync(new Empty(), cancellationToken: ct)
                : await _client.GetUpdateInfoAsync(new Empty(), cancellationToken: ct);
            if (!response.UpdateAvailable) return null;

            UpdateInstallerSource.ValidateDownloadUrl(response.LatestVersion, response.DownloadUrl);
            return new UpdateInfoDto(response.LatestVersion, response.DownloadUrl, response.ReleaseNotes,
                response.PublishedAt.ToDateTime(), UpdateInstallerSource.NormalizeSha256(response.Sha256Hash));
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Failed to get update info");
            throw;
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfoDto updateInfo, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        UpdateInstallerSource.ValidateDownloadUrl(updateInfo.Version, updateInfo.DownloadUrl);
        var expectedHash = UpdateInstallerSource.NormalizeSha256(updateInfo.Sha256Hash);
        var directory = Directory.CreateTempSubdirectory("Axorith-Update-").FullName;
        var path = Path.Combine(directory, "axorith-setup.exe");
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    progress?.Report(0);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(DownloadIdleTimeout);
                    using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength;
                    long downloaded = 0;
                    using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using (var content = await response.Content.ReadAsStreamAsync(timeout.Token))
                    await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                            sha256.AppendData(buffer, 0, read);
                            downloaded += read;
                            timeout.CancelAfter(DownloadIdleTimeout);
                            if (total > 0) progress?.Report(Math.Min(99, (double)downloaded / total.Value * 100));
                        }
                    }

                    if (total.HasValue && downloaded != total.Value)
                        throw new IOException("The update download was incomplete.");
                    if (!string.Equals(Convert.ToHexString(sha256.GetHashAndReset()), expectedHash, StringComparison.Ordinal))
                        throw new InvalidDataException("Update verification failed. The installer was not saved; please try again.");

                    _verifiedInstallers[path] = expectedHash;
                    progress?.Report(100);
                    _logger.LogInformation("Update downloaded and SHA256 verified: {Path}", TelemetryGuard.SafePath(path));
                    return path;
                }
                catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested &&
                                           (ex is HttpRequestException or IOException or OperationCanceledException) &&
                                           ex is not InvalidDataException)
                {
                    _logger.LogWarning(ex, "Update download attempt {Attempt} failed; retrying", attempt);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                }
            }
        }
        catch (Exception ex)
        {
            DeleteDownload(path);
            _logger.LogWarning(ex, "Failed to download update");
            throw;
        }
    }

    public async Task InstallUpdateAsync(string installerPath, CancellationToken ct = default)
    {
        if (!_verifiedInstallers.TryGetValue(installerPath, out var expectedHash))
            throw new InvalidOperationException("Download and verify the update before installing it.");

        try
        {
            using var installer = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(installer, ct));
            if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
                throw new InvalidDataException("The installer changed after verification. Download it again.");

            ct.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException("Windows could not start the update installer.");

            // The installer closes Client and Host only when it is ready to replace their files.
            await process.WaitForExitAsync(ct);
            if (process.ExitCode == 1) throw new OperationCanceledException("Update installation was cancelled.");
            if (process.ExitCode != 0) throw new InvalidOperationException($"Update installer failed (exit code {process.ExitCode}).");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Update installation was cancelled.", ex);
        }
        finally
        {
            _verifiedInstallers.TryRemove(installerPath, out _);
            DeleteDownload(installerPath);
        }
    }

    private void DeleteDownload(string path)
    {
        try
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove temporary update: {Path}", TelemetryGuard.SafePath(path));
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        foreach (var path in _verifiedInstallers.Keys)
            if (_verifiedInstallers.TryRemove(path, out _)) DeleteDownload(path);
    }
}
