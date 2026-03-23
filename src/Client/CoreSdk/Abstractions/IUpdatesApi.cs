namespace Axorith.Client.CoreSdk.Abstractions;

public interface IUpdatesApi
{
    Task<UpdateInfoDto?> GetUpdateInfoAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets update info without version check — usable even when client/host versions are incompatible.
    /// </summary>
    Task<UpdateInfoDto?> GetLatestUpdateInfoAsync(CancellationToken ct = default);

    Task<string> DownloadUpdateAsync(UpdateInfoDto updateInfo, IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task InstallUpdateAsync(string installerPath, CancellationToken ct = default);
}

public record UpdateInfoDto(
    string Version,
    string DownloadUrl,
    string ReleaseNotes,
    DateTime PublishedAt,
    string? Sha256Hash = null,
    string Platform = "win",
    string Architecture = "x64",
    string InstallerType = "exe");