namespace Axorith.Shared.Platform;

public interface IAutoStartManager
{
    bool IsAutoStartEnabled { get; }

    bool EnableAutoStart(bool startMinimized = true);

    bool DisableAutoStart();

    bool IsStartMinimized { get; }
}
