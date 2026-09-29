using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Axorith.Core.Tests.Helpers;

internal sealed class PostHogTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _payloads = new();
    private readonly Task _listenTask;

    public string Host { get; }
    public IReadOnlyCollection<string> Payloads => _payloads.ToArray();

    public PostHogTestServer()
    {
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Host = $"http://127.0.0.1:{port}/";
        _listenTask = ListenAsync(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _listenTask.ConfigureAwait(false);
        }
        catch (SocketException) when (_stop.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        _stop.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                var body = await ReadRequestBodyAsync(reader, cancellationToken).ConfigureAwait(false);
                _payloads.Enqueue(body);
                var response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<string> ReadRequestBodyAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var contentLength = 0;
        var isChunked = false;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } line)
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var name = line[..separator];
            var value = line[(separator + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(value, CultureInfo.InvariantCulture);
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                isChunked = value.Contains("chunked", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (isChunked) return await ReadChunkedBodyAsync(reader, cancellationToken).ConfigureAwait(false);

        var body = new char[contentLength];
        var read = 0;
        while (read < body.Length)
        {
            read += await reader.ReadAsync(body.AsMemory(read), cancellationToken).ConfigureAwait(false);
        }

        return new string(body);
    }

    private static async Task<string> ReadChunkedBodyAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var body = new StringBuilder();
        while (true)
        {
            var sizeLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var size = int.Parse(sizeLine!.Split(';', 2)[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (size == 0)
            {
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 }) { }
                return body.ToString();
            }

            var chunk = new char[size];
            var read = 0;
            while (read < chunk.Length)
            {
                read += await reader.ReadAsync(chunk.AsMemory(read), cancellationToken).ConfigureAwait(false);
            }
            body.Append(chunk);
            await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
