namespace Axorith.Shared.Platform;

public interface IProcessBlocker : IDisposable
{
    bool IsMonitoring { get; }

    List<string> Block(IEnumerable<string> processNames);

    List<string> Block(IEnumerable<string> processNames, bool rejectRunning) =>
        rejectRunning
            ? throw new NotSupportedException("This platform cannot safely reject already running blocked applications.")
            : Block(processNames);

    List<string> AllowOnly(IEnumerable<string> processNames);

    List<string> AllowOnly(IEnumerable<string> processNames, bool rejectRunning) =>
        rejectRunning
            ? throw new NotSupportedException("This platform cannot safely reject already running applications outside the allowlist.")
            : AllowOnly(processNames);

    void Unblock(string processName);

    void UnblockAll();

    event Action<string>? ProcessBlocked;
}
