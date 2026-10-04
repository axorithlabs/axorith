using System.Text;

namespace Axorith.Shared.Utils;

public sealed class SecureMemoryString : IDisposable
{
    private byte[]? _data;
    private readonly Lock _lock = new();
    private bool _disposed;

    public SecureMemoryString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _data = Encoding.UTF8.GetBytes(value);
    }

    public string GetValue()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(SecureMemoryString));

            if (_data == null)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(_data);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_data != null)
            {
                Array.Clear(_data, 0, _data.Length);
                _data = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            Clear();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    ~SecureMemoryString()
    {
        Dispose();
    }
}
