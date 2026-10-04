namespace Axorith.Shared.Platform;

public interface IProcessBlocker : IDisposable
{
    bool IsMonitoring { get; }

    List<string> Block(IEnumerable<string> processNames);

    List<string> AllowOnly(IEnumerable<string> processNames);

    void Unblock(string processName);

    void UnblockAll();

    event Action<string>? ProcessBlocked;
}
