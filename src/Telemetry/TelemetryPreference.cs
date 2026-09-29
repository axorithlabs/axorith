using Axorith.Shared.Utils;

namespace Axorith.Telemetry;

public static class TelemetryPreference
{
    public static string Path { get; } = System.IO.Path.Combine(ApplicationPaths.Config, "telemetry-preference.txt");

    public static bool ReadOrDefault(bool defaultValue) => TryRead(out var enabled) ? enabled : defaultValue;

    public static bool TryRead(out bool enabled)
    {
        try
        {
            var value = File.ReadAllText(Path).Trim();
            if (bool.TryParse(value, out enabled)) return true;
        }
        catch
        {
        }

        enabled = false;
        return false;
    }

    public static bool Save(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, enabled ? "true" : "false");
            return true;
        }
        catch
        {
            // Telemetry preference persistence must not prevent settings from being saved.
            return false;
        }
    }

    public static FileSystemWatcher Watch(Action<bool> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(Path)!, System.IO.Path.GetFileName(Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName
        };
        FileSystemEventHandler handler = (_, _) => Notify();
        RenamedEventHandler renamedHandler = (_, _) => Notify();
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Renamed += renamedHandler;
        watcher.EnableRaisingEvents = true;
        return watcher;

        void Notify()
        {
            if (TryRead(out var enabled)) changed(enabled);
        }
    }
}
