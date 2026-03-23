namespace Axorith.Host.Models;

/// <summary>
///     Contains information about an available update.
/// </summary>
public sealed record UpdateInfo(
    string Version,
    string ReleaseNotes,
    string DownloadUrl,
    string Sha256,
    string Platform,
    string Architecture,
    string InstallerType,
    DateTime PublishedAt);
