using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Axorith.Shared.Utils;

namespace Axorith.Client.Services;

public sealed class WindowStateManager
{
    private readonly string _stateFilePath;
    private const long MaxStateFileSizeBytes = 1 * 1024 * 1024; // 1 MB max

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        MaxDepth = 32 // Prevent stack overflow from deeply nested JSON
    };

    public WindowStateManager()
    {
        _stateFilePath = Path.Combine(ApplicationPaths.Config, "window_state.json");
    }

    internal WindowStateManager(string stateFilePath) => _stateFilePath = stateFilePath;

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
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
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
            ConstrainSizeToScreen(window, window.Screens.Primary, window.Width, window.Height);
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

            if (state is null)
                return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var screen = window.Screens.ScreenFromPoint(new PixelPoint(state.X, state.Y)) ?? window.Screens.Primary;
            ConstrainSizeToScreen(window, screen, state.Width, state.Height);

            if (screen is { WorkingArea.Width: > 0, WorkingArea.Height: > 0 } &&
                double.IsFinite(screen.Scaling) && screen.Scaling > 0)
            {
                var area = screen.WorkingArea;
                var scale = screen.Scaling;

                window.Position = new PixelPoint(
                    Math.Clamp(state.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(window.Width * scale))),
                    Math.Clamp(state.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(window.Height * scale))));
            }
            else
            {
                window.Position = new PixelPoint(state.X, state.Y);
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

    private static void ConstrainSizeToScreen(Window window, Screen? screen, double width, double height)
    {
        if (screen is { WorkingArea.Width: > 0, WorkingArea.Height: > 0 } &&
            double.IsFinite(screen.Scaling) && screen.Scaling > 0)
        {
            var maxWidth = screen.WorkingArea.Width / screen.Scaling;
            var maxHeight = screen.WorkingArea.Height / screen.Scaling;
            window.MinWidth = Math.Min(window.MinWidth, maxWidth);
            window.MinHeight = Math.Min(window.MinHeight, maxHeight);
            window.Width = Math.Clamp(
                double.IsFinite(width) && width > 0 ? width : window.Width,
                window.MinWidth,
                maxWidth);
            window.Height = Math.Clamp(
                double.IsFinite(height) && height > 0 ? height : window.Height,
                window.MinHeight,
                maxHeight);
        }
        else
        {
            if (double.IsFinite(width) && width > 0)
                window.Width = width;
            if (double.IsFinite(height) && height > 0)
                window.Height = height;
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
