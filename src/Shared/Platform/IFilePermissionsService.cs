namespace Axorith.Shared.Platform;

public interface IFilePermissionsService
{
    void SetRestrictivePermissions(string filePath);
}
