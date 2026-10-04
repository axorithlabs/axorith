namespace Axorith.Shared.Utils;

public static class ApplicationPaths
{
    public const string ApplicationName = "Axorith";

    private static readonly Lazy<string> LazyRoamingRoot = new(() =>
        ResolveRoot("AXORITH_ROAMING_ROOT", Environment.SpecialFolder.ApplicationData));

    private static readonly Lazy<string> LazyLocalRoot = new(() =>
        ResolveRoot("AXORITH_LOCAL_ROOT", Environment.SpecialFolder.LocalApplicationData));

    private static readonly Lazy<string> LazyProgramFiles = new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    private static readonly Lazy<string> LazyProgramFilesX86 = new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    private static readonly Lazy<string> LazyCommonAppData = new(() =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    private static string ResolveRoot(string environmentVariable, Environment.SpecialFolder specialFolder)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(specialFolder), ApplicationName)
            : Path.GetFullPath(configuredRoot);
    }

    public static string RoamingRoot => LazyRoamingRoot.Value;

    public static string LocalRoot => LazyLocalRoot.Value;

    public static string Logs => Path.Combine(RoamingRoot, "logs");

    public static string Presets => Path.Combine(RoamingRoot, "presets");

    public static string Config => Path.Combine(RoamingRoot, "config");

    public static string Modules => Path.Combine(RoamingRoot, "modules");

    public static string SecureStorage => Path.Combine(RoamingRoot, "secure_storage");

    public static string NativeMessaging => Path.Combine(RoamingRoot, "native_messaging");

    public static string NativeMessagingFirefox => Path.Combine(NativeMessaging, "firefox");

    public static string NativeMessagingChrome => Path.Combine(NativeMessaging, "chrome");

    public static string HostInfoFile => Path.Combine(MachineRoot, "host-info.json");

    public static string ProgramFiles => LazyProgramFiles.Value;

    public static string ProgramFilesX86 => LazyProgramFilesX86.Value;

    public static string CommonAppData => LazyCommonAppData.Value;

    public static string MachineRoot => OperatingSystem.IsWindows()
        ? Path.Combine(CommonAppData, ApplicationName)
        : LocalRoot;

    public static string MachineConfig => Path.Combine(MachineRoot, "config");

    public static string LocalSecrets => Path.Combine(LocalRoot, "secrets");

    public static string EnsureDirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string ExpandPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(expanded);
    }
}
