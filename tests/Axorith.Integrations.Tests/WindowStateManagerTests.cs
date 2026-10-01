using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Axorith.Client.Services;
using Axorith.Client.Views;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class WindowStateManagerTests
{
    [AvaloniaFact]
    public void RestoreClampsOversizedOffscreenStateAndSaveCreatesItsDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxorithWindowTests", Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(directory, "config", "window_state.json");
        var window = new Window { Width = 1600, Height = 900 };

        try
        {
            var manager = new WindowStateManager(statePath);
            var screen = window.Screens.Primary;
            Assert.NotNull(screen);

            manager.RestoreWindowState(window);

            Assert.InRange(window.Width, window.MinWidth, screen!.WorkingArea.Width / screen.Scaling);
            Assert.InRange(window.Height, window.MinHeight, screen.WorkingArea.Height / screen.Scaling);

            manager.SaveWindowState(window);
            Assert.True(File.Exists(statePath));

            File.WriteAllText(statePath,
                "{\"X\":-4000,\"Y\":-2000,\"Width\":5000,\"Height\":3000,\"IsMaximized\":false}");

            manager.RestoreWindowState(window);

            Assert.InRange(window.Width, window.MinWidth, screen.WorkingArea.Width / screen.Scaling);
            Assert.InRange(window.Height, window.MinHeight, screen.WorkingArea.Height / screen.Scaling);
            Assert.True(window.Position.X >= screen.WorkingArea.X);
            Assert.True(window.Position.Y >= screen.WorkingArea.Y);
            Assert.True(window.Position.X <= screen.WorkingArea.Right - Math.Ceiling(window.Width * screen.Scaling));
            Assert.True(window.Position.Y <= screen.WorkingArea.Bottom - Math.Ceiling(window.Height * screen.Scaling));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
