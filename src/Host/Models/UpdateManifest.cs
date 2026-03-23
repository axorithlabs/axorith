namespace Axorith.Host.Models;

/// <summary>
///     Root object for parsing release manifest.json.
/// </summary>
public sealed class UpdateManifest
{
    public string Version { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public List<ManifestArtifact> Artifacts { get; init; } = [];
}

/// <summary>
///     A single platform artifact entry in the manifest.
/// </summary>
public sealed class ManifestArtifact
{
    public string Platform { get; init; } = "";
    public string Arch { get; init; } = "";
    public string Type { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha256 { get; init; } = "";
}
