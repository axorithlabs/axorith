# ApplicationLauncher Module

## Overview

**ApplicationLauncher** is an advanced module for Axorith that provides comprehensive application and window management capabilities. It allows you to launch applications, attach to existing processes, control window states, customize window sizes, and manage process lifecycle when sessions end.

## Features

### 🚀 **Process Management**
- **Launch New Process** - Start a fresh instance of an application
- **Attach to Existing** - Connect to an already running process
- **Launch or Attach** - Smart mode that attaches if running, otherwise launches

### 🖼️ **Window Management**
- **Monitor Selection** - Move windows to specific monitors (0-based index)
- **Window States** - Set Normal, Maximized, or Minimized state
- **Custom Sizing** - Specify exact window dimensions (width × height)
- **Auto Focus** - Bring window to foreground automatically

### 🎛️ **Lifecycle Control**
- **Terminate on End** - Close process when session ends
- **Keep Running** - Leave process running after session
- **Safe Termination** - Graceful close with fallback to force kill

## Settings

| Setting | Type | Description |
|---------|------|-------------|
| **Process Mode** | Choice | How to handle the application:<br>• Launch New Process<br>• Attach to Existing<br>• Launch or Attach |
| **Application Path** | File Picker | Path to the executable (.exe) |
| **Launch Arguments** | Text | Command-line arguments for new processes |
| **Target Monitor** | Integer | Monitor index (0 = primary, 1 = secondary, etc.) |
| **Window State** | Choice | Desired window state:<br>• Normal<br>• Maximized<br>• Minimized |
| **Use Custom Window Size** | Checkbox | Enable custom window dimensions |
| **Window Width** | Integer | Custom width in pixels (min: 100) |
| **Window Height** | Integer | Custom height in pixels (min: 100) |
| **Process Lifecycle** | Choice | What happens when session ends:<br>• Terminate on Session End<br>• Keep Running |
| **Bring to Foreground** | Checkbox | Auto-focus window after setup |

## Use Cases

### 1. **Development Environment Setup**
Launch IDE, browser, and terminal on specific monitors with optimal window arrangements:
```
- Visual Studio Code on Monitor 1 (Maximized)
- Chrome on Monitor 2 (Custom size: 1920x1080)
- Windows Terminal on Monitor 1 (Normal, 1280x720)
```

### 2. **Gaming Setup**
Attach to game process and control overlay applications:
```
- Attach to existing game.exe
- Move Discord to Monitor 2 (Normal state)
- Keep apps running after session ends
```

### 3. **Productivity Workflow**
Auto-launch and arrange multiple tools:
```
- Spotify on Monitor 2 (Minimized)
- Slack on Monitor 2 (Normal, custom size)
- Browser on Monitor 1 (Maximized)
```

### 4. **Streaming/Recording**
Manage OBS and companion apps:
```
- Launch OBS Studio with specific settings
- Position chat window on secondary monitor
- Terminate all when session ends
```

## Platform Support

| Platform | Process Launch | Attach Existing | Window Management | Custom Sizing |
|----------|----------------|-----------------|-------------------|---------------|
| Windows | ✅ Full | ✅ Full | ✅ Full | ✅ Full |
| Linux | ✅ Basic | ✅ Basic | ⚠️ Limited | ❌ Not Yet |
| macOS | ✅ Basic | ✅ Basic | ⚠️ Limited | ❌ Not Yet |

**Note:** Windows has full support for all features. Linux/macOS support basic launch and attach, with limited window management.

## Technical Details

### Process Discovery
- **By Name**: Matches process executable name (e.g., `notepad`)
- **By Path**: Exact match of full executable path
- **Window Preference**: When multiple processes found, prefers one with a main window

### Window Configuration Order
1. **Monitor Move** - Move to target monitor
2. **Custom Size** - Apply custom dimensions (if enabled and state is Normal)
3. **Window State** - Set Maximized, Minimized, or Normal
4. **Focus** - Bring to foreground (if enabled and not Minimized)

### Lifecycle Management
- **Attached Processes**: Only closes main window (doesn't kill)
- **Launched Processes**: Graceful close → 2s wait → force kill
- **Keep Running Mode**: Detaches and leaves process untouched

### Error Handling
- Validates file existence before launch
- Logs all operations (Debug, Info, Warning, Error levels)
- Graceful degradation for windows without GUI
- Timeout protection for window initialization (5 seconds)

## Example Configurations

### Minimal (Launch Notepad)
```
Process Mode: Launch New Process
Application Path: C:\Windows\notepad.exe
Target Monitor: 0
Window State: Normal
Process Lifecycle: Terminate on Session End
```

### Advanced (Attach to Chrome with Custom Layout)
```
Process Mode: Launch or Attach
Application Path: C:\Program Files\Google\Chrome\Application\chrome.exe
Launch Arguments: --new-window https://example.com
Target Monitor: 1
Window State: Normal
Use Custom Window Size: ✓
Window Width: 1600
Window Height: 900
Process Lifecycle: Keep Running
Bring to Foreground: ✓
```

### Multi-Monitor Streaming Setup
```
Module Instance 1 (OBS):
  Process Mode: Launch New Process
  Application Path: C:\Program Files\obs-studio\bin\64bit\obs64.exe
  Target Monitor: 0
  Window State: Maximized
  Process Lifecycle: Terminate on Session End

Module Instance 2 (Chat):
  Process Mode: Launch New Process
  Application Path: C:\path\to\streamlabs-chatbot.exe
  Target Monitor: 1
  Window State: Normal
  Use Custom Window Size: ✓
  Window Width: 400
  Window Height: 800
  Process Lifecycle: Keep Running
```

## API Reference

The module uses the following **Axorith.Shared.Platform.PublicApi** methods:

- `FindProcesses(string processNameOrPath)` - Find running processes
- `WaitForWindowInitAsync(Process, timeout, token)` - Wait for window creation
- `MoveWindowToMonitor(IntPtr handle, int index)` - Move to monitor
- `SetWindowState(IntPtr handle, WindowState)` - Set Maximized/Minimized/Normal
- `SetWindowSize(IntPtr handle, int width, int height)` - Resize window
- `FocusWindow(IntPtr handle)` - Bring to foreground
- `GetMonitorCount()` - Get available monitor count

## Logging

All operations are logged with structured logging:
```csharp
_logger.LogInfo("ApplicationLauncher starting in {Mode} mode for {AppPath}", mode, appPath);
_logger.LogDebug("Configuring window (Handle: {Handle})", windowHandle);
_logger.LogWarning("Process window did not appear within timeout.");
_logger.LogError(ex, "Failed to launch process: {Path}", path);
```

## Best Practices

✅ **DO**
- Use full paths for Application Path
- Test process attachment before relying on it
- Use "Launch or Attach" for idempotent sessions
- Validate custom sizes (min 100x100)
- Use "Keep Running" for system services

❌ **DON'T**
- Launch system-critical processes
- Use very small custom sizes (< 100px)
- Attach to unstable/crashing processes
- Mix Minimized state with Custom Size
- Rely on exact timing for window init

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Process not found | Verify full executable path |
| Window not moving | Check monitor index (use `GetMonitorCount()`) |
| Custom size not applied | Ensure Window State is "Normal" |
| Process won't terminate | Check if it's an elevated/system process |
| Window appears on wrong monitor | Increase timeout or check multi-monitor setup |

## Version History

### v1.0.0 (Current)
- ✨ Initial release with full Windows support
- ✨ Process Mode: Launch New, Attach Existing, Launch or Attach
- ✨ Window Management: Monitor, State, Custom Size, Focus
- ✨ Lifecycle Management: Terminate or Keep Running
- ✨ Cross-platform foundation (Windows full, Linux/macOS basic)

## Contributing

To extend this module:
1. Add new settings in the constructor
2. Implement logic in `OnSessionStartAsync`
3. Update `ConfigureWindowAsync` for window operations
4. Add lifecycle logic in `OnSessionEndAsync`
5. Test on all supported platforms

## License

Part of the Axorith project. See main LICENSE file.

---

**Built with ❤️ by the Axorith team**  
**Senior C# Dev approved ✅**
