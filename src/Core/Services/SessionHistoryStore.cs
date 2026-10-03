using System.Text.Json;
using Axorith.Core.Models;

namespace Axorith.Core.Services;

public sealed class SessionHistoryStore(string? path)
{
    private readonly Lock _gate = new();
    // Hosts supply their permanent config path. Standalone managers also use disk, in an isolated directory.
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(Path.GetTempPath(), "Axorith",
        Guid.NewGuid().ToString("N"), "session-history.json"));

    public IReadOnlyList<SessionActivity> Read()
    {
        lock (_gate)
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<SessionActivity>>(File.ReadAllText(_path))
                  ?? throw new InvalidDataException("Session history is empty.")
                : [];
        }
    }

    public void Add(SessionActivity activity)
    {
        lock (_gate)
        {
            var updated = Read().ToList();
            if (updated.Contains(activity)) return;
            updated.Add(activity);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, updated);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
