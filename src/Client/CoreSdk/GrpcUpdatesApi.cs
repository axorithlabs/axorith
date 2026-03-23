using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.CoreSdk;

public class GrpcUpdatesApi : IUpdatesApi
{
    private const int MaxSha256Retries = 3;

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

            return MapResponseToDto(response);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to get update info");
            return null;
        }
    }

    public async Task<UpdateInfoDto?> GetLatestUpdateInfoAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetLatestUpdateInfoAsync(new Empty(), cancellationToken: ct);

            if (!response.UpdateAvailable)
            {
                return null;
            }

            return MapResponseToDto(response);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to get latest update info");
            return null;
        }
    }

    private static UpdateInfoDto MapResponseToDto(UpdateInfoResponse response)
    {
        return new UpdateInfoDto(
            response.LatestVersion,
            response.DownloadUrl,
            response.ReleaseNotes,
            response.PublishedAt.ToDateTime(),
            response.Sha256Hash,
            response.Platform,
            response.Architecture,
            response.InstallerType);
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfoDto updateInfo, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var extension = updateInfo.InstallerType switch
        {
            "AppImage" => ".AppImage",
            "dmg" => ".dmg",
            _ => ".exe"
        };

        // On Linux, avoid /tmp which is often mounted noexec
        // Use ~/.local/share/Axorith/updates/ instead
        var downloadDir = OperatingSystem.IsLinux()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Axorith", "updates")
            : Path.GetTempPath();

        Directory.CreateDirectory(downloadDir);
        var tempPath = Path.Combine(downloadDir, $"Axorith-Setup-{updateInfo.Version}{extension}");

        for (var attempt = 1; attempt <= MaxSha256Retries; attempt++)
        {
            try
            {
                var result = await DownloadAndVerifyAsync(updateInfo, tempPath, progress, ct);
                return result;
            }
            catch (UpdateVerificationException ex) when (attempt < MaxSha256Retries)
            {
                _logger.LogWarning(ex, "SHA256 verification failed on attempt {Attempt}/{Max}, retrying...",
                    attempt, MaxSha256Retries);
                File.Delete(tempPath);
                await Task.Delay(1000 * attempt, ct);
            }
        }

        throw new UpdateVerificationException(
            $"Update verification failed after {MaxSha256Retries} attempts");
    }

    private async Task<string> DownloadAndVerifyAsync(
        UpdateInfoDto updateInfo,
        string tempPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        _logger.LogInformation("Downloading update from {Url} to {Path}", updateInfo.DownloadUrl,
            TelemetryGuard.SafePath(tempPath));

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

        using var sha256 = SHA256.Create();
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

        _logger.LogInformation("Update downloaded. SHA256: {Hash}", computedHash);

        if (!string.IsNullOrWhiteSpace(updateInfo.Sha256Hash))
        {
            if (!string.Equals(computedHash, updateInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "SHA256 mismatch! Expected: {Expected}, Got: {Got}",
                    updateInfo.Sha256Hash, computedHash);
                throw new UpdateVerificationException(
                    $"SHA256 mismatch. Expected: {updateInfo.Sha256Hash}, Got: {computedHash}");
            }

            _logger.LogInformation("SHA256 verification passed");
        }
        else
        {
            _logger.LogWarning("No SHA256 hash provided. Proceeding without integrity check.");
        }

        // Authenticode verification on Windows only
        if (OperatingSystem.IsWindows() && updateInfo.InstallerType == "exe")
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

    public Task InstallUpdateAsync(string installerPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException("Installer not found", installerPath);
            }

            _logger.LogInformation("Starting installer: {Path}", TelemetryGuard.SafePath(installerPath));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InstallWindows(installerPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                InstallLinux(installerPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                InstallMacOs(installerPath);
            }
            else
            {
                throw new PlatformNotSupportedException(
                    $"Platform {RuntimeInformation.OSDescription} is not supported for auto-update");
            }

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

    private static void InstallWindows(string installerPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(startInfo);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void InstallLinux(string installerPath)
    {
        // Make the AppImage executable
        File.SetUnixFileMode(installerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        // If the installer was downloaded to /tmp (old code path), copy it to a persistent location
        var currentExe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(currentExe) &&
            currentExe.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            // Replace the old AppImage with the new one
            try
            {
                // File.Copy on a running binary fails with ETXTBSY on Linux.
                // File.Delete removes the inode reference while the process keeps running,
                // then File.Move atomically installs the new binary.
                File.Delete(currentExe);
                File.Move(installerPath, currentExe);
                File.SetUnixFileMode(currentExe,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

                // Launch the newly installed AppImage
                Process.Start(new ProcessStartInfo { FileName = currentExe, UseShellExecute = false });
                return;
            }
            catch (Exception ex)
            {
                // Fall through to launch the downloaded copy directly
                Console.Error.WriteLine($"Failed to replace AppImage at {currentExe}: {ex.Message}");
            }
        }

        // Fallback: launch the downloaded AppImage directly
        Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = false });
    }

    private static void InstallMacOs(string installerPath)
    {
        // DMG: attach, copy .app to /Applications, detach, launch
        var attachProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "hdiutil",
            Arguments = $"attach \"{installerPath}\" -nobrowse",
            UseShellExecute = false,
            RedirectStandardOutput = true
        });

        attachProcess?.WaitForExit(30000);

        // Parse hdiutil output to get actual mount point
        // Output format: /dev/disk2s1  Apple_HFS  /Volumes/Axorith
        // or plist XML when using -plist flag
        var hdiutilOutput = attachProcess?.StandardOutput.ReadToEnd() ?? string.Empty;
        var mountPoint = ParseHdiutilMountPoint(hdiutilOutput);

        if (mountPoint != null && Directory.Exists(mountPoint))
        {
            // Copy .app to /Applications
            var appDir = Directory.GetDirectories(mountPoint, "*.app").FirstOrDefault();
            if (appDir != null)
            {
                var destPath = Path.Combine("/Applications", Path.GetFileName(appDir));

                // Remove old .app bundle first to avoid merge (which breaks code signature)
                if (Directory.Exists(destPath))
                {
                    var rmProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "rm",
                        Arguments = $"-rf \"{destPath}\"",
                        UseShellExecute = false
                    });
                    rmProcess?.WaitForExit(30000);
                }

                var copyProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "cp",
                    Arguments = $"-R \"{appDir}\" /Applications/",
                    UseShellExecute = false
                });
                copyProcess?.WaitForExit(30000);

                // Launch the app
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{destPath}\"",
                    UseShellExecute = false
                });
            }

            // Detach
            Process.Start(new ProcessStartInfo
            {
                FileName = "hdiutil",
                Arguments = $"detach \"{mountPoint}\" -quiet",
                UseShellExecute = false
            })?.WaitForExit(10000);
        }
    }

    /// <summary>
    ///     Parses hdiutil attach output to find the actual mount point.
    ///     Output format: "/dev/disk2s1  Apple_HFS  /Volumes/Axorith"
    ///     When a volume name already exists, macOS appends a number: "/Volumes/Axorith 1"
    /// </summary>
    private static string? ParseHdiutilMountPoint(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        // Look for a line containing "/Volumes/" — the mount point is the last column
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var volumesIndex = line.IndexOf("/Volumes/", StringComparison.Ordinal);
            if (volumesIndex >= 0)
            {
                var mountPath = line[volumesIndex..].Trim();
                if (Directory.Exists(mountPath))
                {
                    return mountPath;
                }
            }
        }

        // Fallback: try default name
        var defaultPath = "/Volumes/Axorith";
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    private bool VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            using var cert2 = X509CertificateLoader.LoadCertificateFromFile(filePath);

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

            var chain = new X509Chain
            {
                ChainPolicy =
                {
                    RevocationMode = X509RevocationMode.Online,
                    RevocationFlag = X509RevocationFlag.EntireChain,
                    VerificationFlags = X509VerificationFlags.NoFlag
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
        catch (CryptographicException ex)
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
}

/// <summary>
///     Exception thrown when SHA256 verification fails after all retries.
/// </summary>
public class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message) { }
    public UpdateVerificationException(string message, Exception innerException) : base(message, innerException) { }
}