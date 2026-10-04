namespace Axorith.Shared.Platform;

public interface ISystemNotificationService
{
    Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null);
}
