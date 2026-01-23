namespace Axorith.Shared.Platform;

/// <summary>
///     Provides platform-specific file permission management.
/// </summary>
public interface IFilePermissionsService
{
    /// <summary>
    ///     Sets restrictive permissions on a file so only the current user can access it.
    ///     On Windows: Sets ACL to allow only current user with FullControl.
    ///     On Unix: Sets file mode to 0600 (user read/write only).
    /// </summary>
    /// <param name="filePath">Path to the file to secure.</param>
    void SetRestrictivePermissions(string filePath);
}
