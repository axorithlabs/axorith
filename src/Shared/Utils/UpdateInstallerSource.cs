namespace Axorith.Shared.Utils;

public static class UpdateInstallerSource
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/axorithlabs/axorith/releases/latest";

    public static Uri GetDownloadUri(string version)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
            throw new InvalidDataException("The update version is invalid.");

        return new Uri($"https://github.com/axorithlabs/axorith/releases/download/v{version}/axorith-setup.exe");
    }

    public static void ValidateDownloadUrl(string version, string downloadUrl)
    {
        if (!string.Equals(downloadUrl, GetDownloadUri(version).AbsoluteUri, StringComparison.Ordinal))
            throw new InvalidDataException("The update does not come from the official Axorith release.");
    }

    public static string NormalizeSha256(string? digest)
    {
        if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            digest = digest[7..];

        if (digest is not { Length: 64 } || !digest.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("The update has no valid SHA256 checksum. Restart Axorith Host and try again.");

        return digest.ToUpperInvariant();
    }
}
