namespace Axorith.Shared.Platform;

/// <summary>
///     Defines a contract for managing Browser Native Messaging Host registration.
///     This service is responsible for generating manifest files and updating
///     OS-specific configuration (e.g., Windows Registry) to allow browsers
///     to communicate with the Axorith Shim process.
/// </summary>
public interface INativeMessagingManager
{
    /// <summary>
    ///     Registers the Native Messaging Host for Firefox.
    ///     Generates the JSON manifest and registers its location in the OS.
    /// </summary>
    /// <param name="hostName">
    ///     The unique name of the native host (e.g., "axorith" or "axorith.dev").
    ///     This must match the name used in the browser extension's connectNative() call.
    /// </param>
    /// <param name="executablePath">
    ///     The absolute path to the Native Messaging Host executable (Axorith.Shim.exe).
    /// </param>
    /// <param name="allowedExtensions">
    ///     A list of browser extension IDs that are permitted to communicate with this host.
    /// </param>
    void RegisterFirefoxHost(string hostName, string executablePath, string[] allowedExtensions);
}