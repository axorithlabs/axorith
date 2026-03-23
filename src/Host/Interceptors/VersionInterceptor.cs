using System.Reflection;
using Axorith.Contracts;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Axorith.Host.Interceptors;

/// <summary>
///     Global gRPC interceptor that validates client version compatibility before processing requests.
///     Runs BEFORE AuthenticationInterceptor to fail fast on version mismatches.
///     Certain methods (e.g. GetLatestUpdateInfo) bypass version check to allow update flow even when incompatible.
/// </summary>
public class VersionInterceptor(
    ILogger<VersionInterceptor> logger) : Interceptor
{
    /// <summary>
    ///     Methods that are allowed to bypass version validation.
    ///     These are callable even when client and host versions are incompatible.
    /// </summary>
    private static readonly HashSet<string> VersionBypassMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetLatestUpdateInfo",
        "CheckForUpdates"
    };

    private static readonly Lazy<string> HostVersion = new(() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    /// <summary>
    ///     Gets the current Host version string.
    /// </summary>
    public static string CurrentHostVersion => HostVersion.Value;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ValidateVersion(context);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateVersion(context);
        return await continuation(requestStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateVersion(context);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateVersion(context);
        await continuation(requestStream, responseStream, context);
    }

    private void ValidateVersion(ServerCallContext context)
    {
        var clientVersion = context.RequestHeaders.GetValue(AuthConstants.VersionHeaderName);

        if (string.IsNullOrEmpty(clientVersion))
        {
            logger.LogWarning("Client version header missing from {Peer}", context.Peer);
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Missing required '{AuthConstants.VersionHeaderName}' header."));
        }

        // Dev builds are always compatible
        if (clientVersion == "dev")
        {
            logger.LogDebug("Client {Peer} running dev build, skipping version check", context.Peer);
            return;
        }

        var hostVersion = HostVersion.Value;

        if (!IsCompatible(clientVersion, hostVersion))
        {
            logger.LogWarning(
                "Client version {ClientVersion} incompatible with Host version {HostVersion} (peer: {Peer})",
                clientVersion, hostVersion, context.Peer);

            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Client version {clientVersion} is incompatible with Host version {hostVersion}. Please update."));
        }

        logger.LogDebug("Client version {ClientVersion} compatible with Host {HostVersion}",
            clientVersion, hostVersion);
    }

    /// <summary>
    ///     Determines if the client version is compatible with the host version.
    ///     Compatible IF: clientMajor == hostMajor AND hostMinor >= clientMinor.
    /// </summary>
    internal static bool IsCompatible(string clientVersion, string hostVersion)
    {
        if (!TryParseMajorMinor(clientVersion, out var clientMajor, out var clientMinor))
        {
            // If we can't parse client version, reject it
            return false;
        }

        if (!TryParseMajorMinor(hostVersion, out var hostMajor, out var hostMinor))
        {
            // If we can't parse host version, allow it (shouldn't happen)
            return true;
        }

        // Compatible IF: same major AND host minor >= client minor
        return clientMajor == hostMajor && hostMinor >= clientMinor;
    }

    private static bool TryParseMajorMinor(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        // Handle versions like "1.2.3", "1.2.0-dev", "1.2.0+build"
        var dashIndex = version.IndexOf('-');
        var plusIndex = version.IndexOf('+');
        var endIndex = version.Length;

        switch (dashIndex)
        {
            case >= 0 when plusIndex >= 0:
                endIndex = Math.Min(dashIndex, plusIndex);
                break;
            case >= 0:
                endIndex = dashIndex;
                break;
            default:
            {
                if (plusIndex >= 0)
                {
                    endIndex = plusIndex;
                }

                break;
            }
        }

        var cleanVersion = version[..endIndex];
        var parts = cleanVersion.Split('.');

        return parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
    }
}
