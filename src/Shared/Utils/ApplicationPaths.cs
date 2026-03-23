namespace Axorith.Shared.Utils;

/// <summary>
///     Provides centralized access to all application-specific file system paths.
///     This class ensures consistent path resolution across all Axorith components
///     and eliminates path duplication throughout the codebase.
/// </summary>
/// <remarks>
///     <para>
///         All paths are resolved relative to the user's application data directories
///         as defined by the operating system. On Windows, this typically resolves to:
///         <list type="bullet">
///             <item>
///                 <description>AppData: %APPDATA%\Axorith (e.g., C:\Users\{user}\AppData\Roaming\Axorith)</description>
///             </item>
///             <item>
///                 <description>LocalAppData: %LOCALAPPDATA%\Axorith (e.g., C:\Users\{user}\AppData\Local\Axorith)</description>
///             </item>
///         </list>
///     </para>
///     <para>
///         On Linux/macOS, paths follow XDG conventions where applicable.
///     </para>
///     <para>
///         This class is thread-safe and all properties are lazily initialized on first access.
///     </para>
/// </remarks>
/// <example>
///     <code>
///         // Get the logs directory path
///         var logsPath = ApplicationPaths.Logs;
///         
///         // Get the full path to a specific log file
///         var logFile = Path.Combine(ApplicationPaths.Logs, "app.log");
///         
///         // Get config directory for storing application settings
///         var configPath = ApplicationPaths.Config;
///     </code>
/// </example>
public static class ApplicationPaths
{
    /// <summary>
    ///     The application name used as the root folder name in all paths.
    /// </summary>
    public const string ApplicationName = "Axorith";

    private static readonly Lazy<string> LazyRoamingRoot = new(() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName));

    private static readonly Lazy<string> LazyLocalRoot = new(() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationName));

    private static readonly Lazy<string> LazyProgramFiles = new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    private static readonly Lazy<string> LazyProgramFilesX86 = new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    private static readonly Lazy<string> LazyCommonAppData = new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>
    ///     Gets the root application data directory (roaming profile).
    ///     This is the primary location for user-specific application data that should roam with the user profile.
    /// </summary>
    /// <value>
    ///     The full path to the Axorith roaming application data directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith
    /// </value>
    /// <remarks>
    ///     Use this for data that should be available across multiple machines in a domain environment.
    ///     This includes user preferences, presets, and configuration files.
    /// </remarks>
    public static string RoamingRoot => LazyRoamingRoot.Value;

    /// <summary>
    ///     Gets the local application data directory (non-roaming).
    ///     This is for machine-specific data that should not roam with the user profile.
    /// </summary>
    /// <value>
    ///     The full path to the Axorith local application data directory.
    ///     Example: C:\Users\{user}\AppData\Local\Axorith
    /// </value>
    /// <remarks>
    ///     Use this for cache files, temporary data, or large files that shouldn't be synchronized.
    /// </remarks>
    public static string LocalRoot => LazyLocalRoot.Value;

    /// <summary>
    ///     Gets the directory path for application log files.
    /// </summary>
    /// <value>
    ///     The full path to the logs directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\logs
    /// </value>
    /// <remarks>
    ///     Log files are stored in the roaming profile to allow centralized log collection
    ///     in enterprise environments. The directory is created automatically when accessed
    ///     via <see cref="EnsureDirectoryExists" />.
    /// </remarks>
    public static string Logs => Path.Combine(RoamingRoot, "logs");

    /// <summary>
    ///     Gets the directory path for user presets and saved configurations.
    /// </summary>
    /// <value>
    ///     The full path to the presets directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\presets
    /// </value>
    /// <remarks>
    ///     Presets contain user-defined session configurations and module settings.
    ///     These are stored in the roaming profile to allow users to access their presets
    ///     from any machine in a domain environment.
    /// </remarks>
    public static string Presets => Path.Combine(RoamingRoot, "presets");

    /// <summary>
    ///     Gets the directory path for application configuration files.
    /// </summary>
    /// <value>
    ///     The full path to the config directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\config
    /// </value>
    /// <remarks>
    ///     Configuration files include authentication tokens, application settings,
    ///     and other persistent configuration data.
    /// </remarks>
    public static string Config => Path.Combine(RoamingRoot, "config");

    /// <summary>
    ///     Gets the directory path for loadable modules.
    /// </summary>
    /// <value>
    ///     The full path to the modules directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\modules
    /// </value>
    /// <remarks>
    ///     This directory contains user-installed modules that extend Axorith functionality.
    ///     System modules may be located in different directories (e.g., alongside the executable).
    /// </remarks>
    public static string Modules => Path.Combine(RoamingRoot, "modules");

    /// <summary>
    ///     Gets the directory path for secure storage (encrypted credentials).
    /// </summary>
    /// <value>
    ///     The full path to the secure storage directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\secure_storage
    /// </value>
    /// <remarks>
    ///     This directory contains encrypted credential files protected by platform-specific
    ///     encryption (DPAPI on Windows, Secret Service on Linux, Keychain on macOS).
    ///     Files in this directory should never be manually modified.
    /// </remarks>
    public static string SecureStorage => Path.Combine(RoamingRoot, "secure_storage");

    /// <summary>
    ///     Gets the directory path for native messaging manifests and related files.
    /// </summary>
    /// <value>
    ///     The full path to the native messaging directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\native_messaging
    /// </value>
    /// <remarks>
    ///     Contains browser-specific subdirectories (firefox, chrome) with native messaging
    ///     host manifests required for browser extension communication.
    /// </remarks>
    public static string NativeMessaging => Path.Combine(RoamingRoot, "native_messaging");

    /// <summary>
    ///     Gets the directory path for Firefox native messaging manifests.
    /// </summary>
    /// <value>
    ///     The full path to the Firefox native messaging directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\native_messaging\firefox
    /// </value>
    public static string NativeMessagingFirefox => Path.Combine(NativeMessaging, "firefox");

    /// <summary>
    ///     Gets the directory path for Chrome/Chromium native messaging manifests.
    /// </summary>
    /// <value>
    ///     The full path to the Chrome native messaging directory.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\native_messaging\chrome
    /// </value>
    public static string NativeMessagingChrome => Path.Combine(NativeMessaging, "chrome");

    /// <summary>
    ///     Gets the file path for the host information JSON file.
    /// </summary>
    /// <value>
    ///     The full path to the host-info.json file.
    ///     Example: C:\Users\{user}\AppData\Roaming\Axorith\host-info.json
    /// </value>
    /// <remarks>
    ///     This file contains runtime information about the Host process, including
    ///     the dynamically assigned port number. It is created when the Host starts
    ///     and deleted when it shuts down.
    /// </remarks>
    public static string HostInfoFile => Path.Combine(RoamingRoot, "host-info.json");

    /// <summary>
    ///     Gets the Program Files directory path.
    /// </summary>
    /// <value>
    ///     The full path to the Program Files directory.
    ///     Example: C:\Program Files
    /// </value>
    /// <remarks>
    ///     Used for discovering installed applications on Windows.
    /// </remarks>
    public static string ProgramFiles => LazyProgramFiles.Value;

    /// <summary>
    ///     Gets the Program Files (x86) directory path.
    /// </summary>
    /// <value>
    ///     The full path to the Program Files (x86) directory.
    ///     Example: C:\Program Files (x86)
    /// </value>
    /// <remarks>
    ///     Used for discovering 32-bit applications on 64-bit Windows systems.
    /// </remarks>
    public static string ProgramFilesX86 => LazyProgramFilesX86.Value;

    /// <summary>
    ///     Gets the common application data directory (shared across all users).
    /// </summary>
    /// <value>
    ///     The full path to the common application data directory.
    ///     Example: C:\ProgramData
    /// </value>
    /// <remarks>
    ///     Used for application data that should be shared across all users on the machine.
    /// </remarks>
    public static string CommonAppData => LazyCommonAppData.Value;

    /// <summary>
    ///     Gets the IPC endpoint path for local gRPC communication.
    ///     On Unix (Linux/macOS), returns a Unix Domain Socket path.
    ///     On Windows, returns a Named Pipe name.
    /// </summary>
    /// <value>
    ///     The full path to the IPC endpoint.
    ///     Linux/macOS: ~/.local/share/Axorith/axorith.sock
    ///     Windows: axorith-ipc (Named Pipe name)
    /// </value>
    public static string IpcEndpoint =>
        OperatingSystem.IsWindows()
            ? "axorith-ipc"
            : Path.Combine(LocalRoot, "axorith.sock");

    /// <summary>
    ///     Gets the secrets directory path for Linux fallback storage.
    /// </summary>
    /// <value>
    ///     The full path to the secrets directory in local app data.
    ///     Example: ~/.local/share/Axorith/secrets (Linux)
    /// </value>
    /// <remarks>
    ///     Used on Linux when Secret Service is not available.
    ///     This directory has restricted permissions (owner-only access).
    /// </remarks>
    public static string LocalSecrets => Path.Combine(LocalRoot, "secrets");

    /// <summary>
    ///     Ensures that the specified directory exists, creating it if necessary.
    /// </summary>
    /// <param name="path">The directory path to ensure exists.</param>
    /// <returns>The same path that was passed in, for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is null.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have permission to create the directory.</exception>
    /// <exception cref="IOException">Thrown when the directory cannot be created due to an I/O error.</exception>
    /// <example>
    ///     <code>
    ///         // Ensure logs directory exists and get the path
    ///         var logsPath = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.Logs);
    ///         
    ///         // Chain with file operations
    ///         File.WriteAllText(
    ///             Path.Combine(ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.Config), "settings.json"),
    ///             jsonContent);
    ///     </code>
    /// </example>
    public static string EnsureDirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     Combines the roaming root path with additional path segments.
    /// </summary>
    /// <param name="paths">The path segments to combine with the roaming root.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths" /> is null.</exception>
    /// <example>
    ///     <code>
    ///         // Get path to a specific module's data directory
    ///         var modulePath = ApplicationPaths.CombineWithRoaming("modules", "discord", "cache");
    ///         // Result: C:\Users\{user}\AppData\Roaming\Axorith\modules\discord\cache
    ///     </code>
    /// </example>
    public static string CombineWithRoaming(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var allPaths = new string[paths.Length + 1];
        allPaths[0] = RoamingRoot;
        Array.Copy(paths, 0, allPaths, 1, paths.Length);
        return Path.Combine(allPaths);
    }

    /// <summary>
    ///     Combines the local root path with additional path segments.
    /// </summary>
    /// <param name="paths">The path segments to combine with the local root.</param>
    /// <returns>The combined path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths" /> is null.</exception>
    /// <example>
    ///     <code>
    ///         // Get path to a cache directory
    ///         var cachePath = ApplicationPaths.CombineWithLocal("cache", "thumbnails");
    ///         // Result: C:\Users\{user}\AppData\Local\Axorith\cache\thumbnails
    ///     </code>
    /// </example>
    public static string CombineWithLocal(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var allPaths = new string[paths.Length + 1];
        allPaths[0] = LocalRoot;
        Array.Copy(paths, 0, allPaths, 1, paths.Length);
        return Path.Combine(allPaths);
    }

    /// <summary>
    ///     Expands environment variables in a path string and normalizes the result.
    /// </summary>
    /// <param name="path">The path containing environment variables (e.g., %AppData%\Axorith).</param>
    /// <returns>The expanded and normalized path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is null.</exception>
    /// <remarks>
    ///     This method expands Windows-style environment variables (%VAR%) and
    ///     normalizes the path by resolving relative segments and standardizing separators.
    /// </remarks>
    /// <example>
    ///     <code>
    ///         var path = ApplicationPaths.ExpandPath("%AppData%/Axorith/logs");
    ///         // Result: C:\Users\{user}\AppData\Roaming\Axorith\logs
    ///     </code>
    /// </example>
    public static string ExpandPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(expanded);
    }
}