using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Axorith.Integrations.Tests;

[Collection("SiteBlocker extension pipes")]
public sealed class ShimProcessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task BrowserEofOrInvalidNativeFrameTerminatesTheRealShim(int headerBytes)
    {
        using var process = StartShim();
        var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (headerBytes > 0)
            {
                var header = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, 2 * 1024 * 1024);
                await process.StandardInput.BaseStream.WriteAsync(header.AsMemory(0, headerBytes));
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            await output;
            Assert.Equal(0, process.ExitCode);
            Assert.DoesNotContain("Unhandled exception", await error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("edge")]
    [InlineData("chromium")]
    [InlineData("firefox")]
    public async Task RealShimCorrelatesPipeRequestsWithLengthPrefixedBrowserResponses(string browser)
    {
        using var process = StartShim(browser);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await using var pipe = new NamedPipeClientStream(".", Axorith.Shared.Utils.SiteBlockerExtensionIds.ScopePipeName("axorith-nm-pipe-" + browser), PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(deadline.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var id = Guid.NewGuid().ToString();
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "health", requestId = id }));
            while (true)
            {
                var header = new byte[4];
                await process.StandardOutput.BaseStream.ReadExactlyAsync(header, deadline.Token);
                var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
                await process.StandardOutput.BaseStream.ReadExactlyAsync(body, deadline.Token);
                using var request = JsonDocument.Parse(body);
                if (!request.RootElement.TryGetProperty("requestId", out var requestId) || requestId.GetString() != id) continue;
                Assert.Equal("health", request.RootElement.GetProperty("command").GetString());
                break;
            }
            var response = JsonSerializer.Serialize(new { requestId = id, marker = Guid.NewGuid().ToString(), ok = false });
            var bytes = Encoding.UTF8.GetBytes(response);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            await process.StandardInput.BaseStream.WriteAsync(length, deadline.Token);
            await process.StandardInput.BaseStream.WriteAsync(bytes, deadline.Token);
            await process.StandardInput.BaseStream.FlushAsync(deadline.Token);
            Assert.Equal(response, await reader.ReadLineAsync(deadline.Token));
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
        }
    }

    private static Process StartShim(string browser = "chrome")
    {
        var root = Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT") ??
                   Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var executable = Path.Combine(root, "Axorith.Shim", OperatingSystem.IsWindows() ? "Axorith.Shim.exe" : "Axorith.Shim");
        return Process.Start(new ProcessStartInfo(executable, "--browser=" + browser)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        })!;
    }
}
