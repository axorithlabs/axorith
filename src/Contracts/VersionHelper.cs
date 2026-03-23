using System.Reflection;

namespace Axorith.Contracts;

/// <summary>
///     Provides client version information for version handshake between Client and Host.
/// </summary>
public static class VersionHelper
{
    /// <summary>
    ///     Gets the client version string for use in gRPC version headers.
    ///     Returns the assembly informational version, or "dev" if unavailable.
    /// </summary>
    /// <param name="assembly">The assembly to extract the version from. Defaults to the executing assembly.</param>
    /// <returns>A version string suitable for the x-axorith-version header.</returns>
    public static string GetClientVersion(Assembly? assembly = null)
    {
        var asm = assembly ?? Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "dev";
    }
}
