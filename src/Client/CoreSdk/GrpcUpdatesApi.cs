using System.Diagnostics;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Telemetry;
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
                response.PublishedAt.ToDateTime(),
                response.Sha256Hash);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to get update info");
            return null;
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfoDto updateInfo, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"Axorith-Setup-{updateInfo.Version}.exe");

            _logger.LogInformation("Downloading update from {Url} to {Path}", updateInfo.DownloadUrl, TelemetryGuard.SafePath(tempPath));

            using var response =
                await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream =
                new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percentage = (double)downloadedBytes / totalBytes * 100;
                    progress?.Report(percentage);
                }
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var computedHash = Convert.ToHexString(sha256.Hash!);

            _logger.LogInformation("Update downloaded successfully. SHA256: {Hash}", computedHash);

            if (!string.IsNullOrWhiteSpace(updateInfo.Sha256Hash))
            {
                if (!string.Equals(computedHash, updateInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new InvalidOperationException(
                        $"Update file integrity check failed. Expected: {updateInfo.Sha256Hash}, Got: {computedHash}");
                }

                _logger.LogInformation("Update file integrity verified successfully");
            }
            else
            {
                _logger.LogWarning("No SHA256 hash provided for update verification. Proceeding without integrity check.");
            }

            if (OperatingSystem.IsWindows())
            {
                if (!VerifyAuthenticodeSignature(tempPath))
                {
                    File.Delete(tempPath);
                    throw new InvalidOperationException(
                        "Update file signature verification failed. The installer is not signed by a trusted publisher.");
                }
                _logger.LogInformation("Authenticode signature verified successfully");
            }

            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            throw;
        }
    }

    private bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            using var cert2 = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(filePath);
            
            var expectedSubjects = new[]
            {
                "CN=Axorith Labs",
                "CN=AxorithLabs",
                "O=Axorith Labs"
            };

            var subjectMatches = expectedSubjects.Any(expected => 
                cert2.Subject.Contains(expected, StringComparison.OrdinalIgnoreCase));

            if (!subjectMatches)
            {
                _logger.LogError("Certificate subject mismatch. Expected one of: {Expected}, Got: {Actual}", 
                    string.Join(", ", expectedSubjects), cert2.Subject);
                return false;
            }

            var chain = new System.Security.Cryptography.X509Certificates.X509Chain
            {
                ChainPolicy =
                {
                    RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online,
                    RevocationFlag = System.Security.Cryptography.X509Certificates.X509RevocationFlag.EntireChain,
                    VerificationFlags = System.Security.Cryptography.X509Certificates.X509VerificationFlags.NoFlag
                }
            };

            var isValid = chain.Build(cert2);

            if (!isValid)
            {
                _logger.LogError("Certificate chain validation failed. Status: {Status}", 
                    string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation)));
                return false;
            }

            var now = DateTime.Now;
            if (now < cert2.NotBefore || now > cert2.NotAfter)
            {
                _logger.LogError("Certificate is expired or not yet valid. Valid from {From} to {To}", 
                    cert2.NotBefore, cert2.NotAfter);
                return false;
            }

            return true;
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "File is not signed or signature is invalid");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Authenticode signature");
            return false;
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

            _logger.LogInformation("Starting installer: {Path}", TelemetryGuard.SafePath(installerPath));

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