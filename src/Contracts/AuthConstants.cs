namespace Axorith.Contracts;

/// <summary>
///     Shared constants for client-host communication contracts.
/// </summary>
public static class AuthConstants
{
    /// <summary>
    ///     The HTTP/2 header name used for the authentication token.
    /// </summary>
    public const string TokenHeaderName = "x-axorith-auth-token";

    /// <summary>
    ///     The filename where the host writes the ephemeral session token.
    /// </summary>
    public const string TokenFileName = ".auth_token";
}