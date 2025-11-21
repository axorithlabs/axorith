using Avalonia.Controls;

namespace Axorith.Client.Services;

public interface IWindowStateManager
{
    void SaveWindowState(Window window);
    void RestoreWindowState(Window window);
}