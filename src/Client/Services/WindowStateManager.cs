using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace Axorith.Client.Services;

public interface IWindowStateManager
{
    void SaveWindowState(Window window);
    void RestoreWindowState(Window window);
}

public class WindowStateManager : IWindowStateManager
{
    private readonly string _stateFilePath;

    public WindowStateManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Axorith");

        Directory.CreateDirectory(appDataPath);
        _stateFilePath = Path.Combine(appDataPath, "window_state.json");
    }

    public void SaveWindowState(Window window)
    {
        try
        {
            var state = new WindowState
            {
                X = window.Position.X,
                Y = window.Position.Y,
                Width = window.Width,
                Height = window.Height,
                IsMaximized = window.WindowState == Avalonia.Controls.WindowState.Maximized
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // Ignore errors - not critical
        }
    }

    public void RestoreWindowState(Window window)
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return;

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<WindowState>(json);

            switch (state)
            {
                case null:
                    return;
                case { X: >= 0, Y: >= 0 }:
                    window.Position = new PixelPoint(state.X, state.Y);
                    break;
            }

            if (state is { Width: > 0, Height: > 0 })
            {
                window.Width = state.Width;
                window.Height = state.Height;
            }

            if (state.IsMaximized) window.WindowState = Avalonia.Controls.WindowState.Maximized;
        }
        catch
        {
            // Ignore errors - use default window state
        }
    }

    private class WindowState
    {
        public int X { get; init; }
        public int Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public bool IsMaximized { get; init; }
    }
}