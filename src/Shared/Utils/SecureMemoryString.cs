using System.Text;

namespace Axorith.Shared.Utils;

/// <summary>
///     Provides secure in-memory storage for sensitive strings like tokens.
///     Automatically zeros memory on disposal to prevent secrets from lingering in heap.
/// </summary>
public sealed class SecureMemoryString : IDisposable
{
    private byte[]? _data;
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of SecureMemoryString with the specified value.
    ///     The value is immediately converted to bytes and stored in memory.
    /// </summary>
    /// <param name="value">The string value to store securely.</param>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    public SecureMemoryString(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        _data = Encoding.UTF8.GetBytes(value);
    }

    /// <summary>
    ///     Retrieves the stored value as a string.
    ///     The returned string is not protected and should be used immediately and not stored.
    /// </summary>
    /// <returns>The decrypted string value.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
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

    /// <summary>
    ///     Clears the stored value from memory by zeroing the underlying byte array.
    ///     After calling this method, GetValue will return an empty string.
    /// </summary>
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

    /// <summary>
    ///     Releases all resources used by the SecureMemoryString and clears sensitive data from memory.
    /// </summary>
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

    /// <summary>
    ///     Finalizer that ensures sensitive data is cleared even if Dispose is not called explicitly.
    /// </summary>
    ~SecureMemoryString()
    {
        Dispose();
    }
}