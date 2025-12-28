using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Axorith.Client.Services.Abstractions;

namespace Axorith.Client.Services;

public class WindowStateManager : IWindowStateManager
{
    private readonly string _stateFilePath;
    private const long MaxStateFileSizeBytes = 1 * 1024 * 1024; // 1 MB max

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        MaxDepth = 32 // Prevent stack overflow from deeply nested JSON
    };

    public WindowStateManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Axorith");

        Directory.CreateDirectory(appDataPath);
        _stateFilePath = Path.Combine(appDataPath, "config", "window_state.json");
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
            {
                return;
            }

            var fileInfo = new FileInfo(_stateFilePath);
            if (fileInfo.Length > MaxStateFileSizeBytes)
            {
                return; // File too large, use default state
            }

            var json = File.ReadAllText(_stateFilePath);
            // V5611: System.Text.Json is safe - no polymorphic deserialization or type name handling
            // File size and MaxDepth are validated to prevent DoS attacks
            var state = JsonSerializer.Deserialize<WindowState>(json, DeserializeOptions); //-V5611

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

            if (state.IsMaximized)
            {
                window.WindowState = Avalonia.Controls.WindowState.Maximized;
            }
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