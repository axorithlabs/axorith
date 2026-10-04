namespace Axorith.Shared.Platform;

public interface INativeMessagingManager
{
    void RegisterFirefoxHost(string hostName, string executablePath, string[] allowedExtensions);

    void RemoveFirefoxExtensionPolicy(string extensionId);

    void RegisterChromeHost(string hostName, string executablePath,
        IReadOnlyDictionary<string, string[]> allowedOriginsByBrowser);
}
