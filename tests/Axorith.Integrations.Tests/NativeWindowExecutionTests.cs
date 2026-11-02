using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Axorith.Client.Services;
using Axorith.Client.Views;
using ReactiveUI.Avalonia;
using Xunit;
using Xunit.Abstractions;

namespace Axorith.Integrations.Tests;

[Collection("Dashboard Host")]
public sealed class NativeWindowExecutionTests(ITestOutputHelper output)
{
    [DisposableWindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task ToastsPreserveNativeKeyboardFocusAndFitTheRealWorkingArea()
    {
        var installedClient = Path.Combine(Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")!,
            "Axorith.Client", "Axorith.Client.dll");
        // ReadyToRun publishing changes PE bytes while preserving the compiled module identity.
        using var installedFile = File.OpenRead(installedClient);
        using var installedPe = new PEReader(installedFile);
        var metadata = installedPe.GetMetadataReader();
        Assert.Equal(typeof(Axorith.Client.App).Module.ModuleVersionId,
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // AvaloniaFact forces Headless; this isolated test process needs the actual Win32 backend.
                Axorith.Client.Program.BuildAvaloniaApp().SetupWithoutStarting();
                ReactiveUI.RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
                ReactiveUI.RxSchedulers.MainThreadScheduler = AvaloniaScheduler.Instance;
                Dispatcher.UIThread.Post(async () =>
                {
                    try { await CheckWindowsAsync(deadline.Token); completed.TrySetResult(); }
                    catch (Exception error) { completed.TrySetException(error); }
                    finally { await deadline.CancelAsync(); }
                });
                Dispatcher.UIThread.MainLoop(deadline.Token);
            }
            catch (Exception error) { completed.TrySetException(error); }
            finally { completed.TrySetCanceled(deadline.Token); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(35)); }
        finally
        {
            await deadline.CancelAsync();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The native UI dispatcher did not stop.");
        }
    }

    private async Task CheckWindowsAsync(CancellationToken cancellationToken)
    {
        var input = new TextBox();
        var editor = new Window { Content = input, Width = 500, Height = 250, Topmost = true };
        using var desktop = new ClassicDesktopStyleApplicationLifetime { MainWindow = editor, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var toasts = new List<DesktopToastWindow>();
        using var opened = Window.WindowOpenedEvent.AddClassHandler(typeof(DesktopToastWindow),
            (sender, _) => toasts.Add((DesktopToastWindow)sender!));
        using var closed = Window.WindowClosedEvent.AddClassHandler(typeof(DesktopToastWindow),
            (sender, _) => toasts.Remove((DesktopToastWindow)sender!));
        using var notifications = new ToastNotificationService();
        using var manager = new DesktopNotificationManager(notifications, desktop);
        try
        {
            editor.Show();
            var editorHandle = editor.TryGetPlatformHandle()!.Handle;
            var editorBounds = Bounds(editor);
            // Windows may deny programmatic activation while the installed Client owns foreground input.
            Assert.True(SetCursorPos((editorBounds.Left + editorBounds.Right) / 2,
                (editorBounds.Top + editorBounds.Bottom) / 2));
            var click = new[]
            {
                new NativeInput { Mouse = new MouseInput { Flags = 0x0002 } },
                new NativeInput { Mouse = new MouseInput { Flags = 0x0004 } }
            };
            Assert.Equal(2u, SendInput(2, click, Marshal.SizeOf<NativeInput>()));
            await Until(() => GetForegroundWindow() == editorHandle);
            editor.Topmost = false;
            input.Focus();
            await Until(() => input.IsFocused);
            SendMessageW(editorHandle, 0x0102 /* WM_CHAR */, 'a', 1);
            await Until(() => input.Text == "a");
            manager.Initialize();
            notifications.Show("First native notification");
            await Until(() => toasts.Count == 1);
            var first = toasts.Single();
            notifications.Show("Second native notification");
            await Until(() => toasts.Count == 2);
            var second = toasts.Single(window => window != first);
            var screen = editor.Screens.ScreenFromWindow(editor)!;
            output.WriteLine($"Native screen: workingArea={screen.WorkingArea}, scale={screen.Scaling}");
            var margin = (int)Math.Ceiling(20 * screen.Scaling);
            var gap = (int)Math.Ceiling(12 * screen.Scaling);
            await Until(() => Math.Abs(Bounds(second).Bottom - (screen.WorkingArea.Bottom - margin)) <= 2 &&
                              Math.Abs(Bounds(first).Bottom + gap - Bounds(second).Top) <= 2);
            foreach (var window in new[] { first, second })
            {
                var bounds = Bounds(window);
                output.WriteLine($"Toast native bounds: ({bounds.Left}, {bounds.Top})-({bounds.Right}, {bounds.Bottom})");
                Assert.InRange(bounds.Left, screen.WorkingArea.X, screen.WorkingArea.Right);
                Assert.InRange(bounds.Top, screen.WorkingArea.Y, screen.WorkingArea.Bottom);
                Assert.InRange(bounds.Right, screen.WorkingArea.X, screen.WorkingArea.Right);
                Assert.InRange(bounds.Bottom, screen.WorkingArea.Y, screen.WorkingArea.Bottom);
                Assert.InRange(Math.Abs(bounds.Right - (screen.WorkingArea.Right - margin)), 0, 2);
                Assert.False(window.IsActive);
            }
            Assert.Equal(editorHandle, GetForegroundWindow());
            Assert.Same(input, editor.FocusManager!.GetFocusedElement());
            SendMessageW(editorHandle, 0x0102, 'b', 1);
            await Until(() => input.Text == "ab");

            second.Close();
            await Until(() => Math.Abs(Bounds(first).Bottom - (screen.WorkingArea.Bottom - margin)) <= 2);
            first.Close();
            notifications.Show(string.Join(' ', Enumerable.Repeat("Long notification body", 500)));
            await Until(() => toasts.Count == 1);
            var longToast = toasts.Single();
            var scroll = longToast.GetVisualDescendants().OfType<ScrollViewer>().First();
            await Until(() => scroll.Extent.Height > scroll.Viewport.Height && scroll.Viewport.Height > 0);
            var longBounds = Bounds(longToast);
            Assert.InRange(longBounds.Top, screen.WorkingArea.Y, screen.WorkingArea.Bottom);
            Assert.InRange(longBounds.Bottom, screen.WorkingArea.Y, screen.WorkingArea.Bottom);
            scroll.ScrollToEnd();
            await Until(() => scroll.Offset.Y > 0);
            Assert.Equal(editorHandle, GetForegroundWindow());
            Assert.Same(input, editor.FocusManager!.GetFocusedElement());
        }
        finally { manager.Dispose(); editor.Close(); }

        async Task Until(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!condition()) await Task.Delay(20, timeout.Token);
        }
    }

    private static NativeRect Bounds(Window window)
    {
        Assert.True(GetWindowRect(window.TryGetPlatformHandle()!.Handle, out var bounds));
        return bounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { public uint Type; public MouseInput Mouse; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, nuint wParam, nint lParam);

    private sealed class DisposableWindowsFactAttribute : FactAttribute
    {
        public DisposableWindowsFactAttribute()
        {
            var disposable = Environment.GetEnvironmentVariable("USERNAME") == "WDAGUtilityAccount" ||
                             Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
                             Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted";
            if (!OperatingSystem.IsWindows() || !disposable ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT")))
                Skip = "Native focus and screen checks require the separate installed-product process in disposable Windows.";
        }
    }
}
