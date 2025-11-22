using Avalonia.Controls;

namespace Axorith.Client.Services.Abstractions;

public interface IWindowStateManager
{
    void SaveWindowState(Window window);
    void RestoreWindowState(Window window);
}