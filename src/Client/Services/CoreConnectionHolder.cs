using Axorith.Client.CoreSdk.Abstractions;

namespace Axorith.Client.Services;

internal sealed class CoreConnectionHolder
{
    private ICoreConnection? _connection;

    public ICoreConnection? Connection => Volatile.Read(ref _connection);

    public ICoreConnection GetRequiredConnection() =>
        Connection ?? throw new InvalidOperationException("Axorith.Host is not connected.");

    public async Task ReplaceAsync(ICoreConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var previous = Interlocked.Exchange(ref _connection, connection);
        if (previous is null || ReferenceEquals(previous, connection))
        {
            return;
        }

        await CloseAsync(previous).ConfigureAwait(false);
    }

    public async Task CloseAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            await CloseAsync(connection).ConfigureAwait(false);
        }
    }

    private static async Task CloseAsync(ICoreConnection connection)
    {
        try
        {
            await connection.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
