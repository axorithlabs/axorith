using System.Text.Json;
using Axorith.Contracts;
using Grpc.Core;
using Grpc.Net.Client;

namespace Axorith.Benchmarks;

internal static class BenchmarkEnvironment
{
    private static readonly object Gate = new();
    private static string? _root;

    public static string Root
    {
        get
        {
            Initialize();
            return _root!;
        }
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_root is not null)
                return;

            _root = Path.Combine(Path.GetTempPath(), "AxorithBenchmarks",
                $"{Environment.ProcessId}-{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable("AXORITH_ROAMING_ROOT",
                Path.Combine(_root, "roaming", "Axorith"));
            Environment.SetEnvironmentVariable("AXORITH_LOCAL_ROOT",
                Path.Combine(_root, "local", "Axorith"));

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    if (Directory.Exists(_root))
                        Directory.Delete(_root, recursive: true);
                }
                catch
                {
                    // The operating system removes any remaining temporary benchmark files later.
                }
            };
        }
    }
    public static GrpcChannel CreateAuthenticatedChannel(Uri address, string token, HttpClient? client = null)
    {
        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            metadata.Add(AuthConstants.TokenHeaderName, token);
            return Task.CompletedTask;
        });
        var options = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials),
            UnsafeUseInsecureChannelCallCredentials = true
        };
        if (client is not null)
            options.HttpClient = client;
        return GrpcChannel.ForAddress(address, options);
    }

    public static async Task<string> WaitForAuthTokenAsync(string path)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(path))
            {
                var token = await File.ReadAllTextAsync(path);
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException("The host did not create its gRPC authentication token.");
    }

    public static Guid InstallModule(string moduleRoot, Type moduleType, string name)
    {
        var id = Guid.NewGuid();
        var directory = Path.Combine(moduleRoot, name.Replace(' ', '_'));
        Directory.CreateDirectory(directory);
        var assemblyName = Path.GetFileName(moduleType.Assembly.Location);
        File.Copy(moduleType.Assembly.Location, Path.Combine(directory, assemblyName));
        File.WriteAllText(Path.Combine(directory, "module.json"), JsonSerializer.Serialize(new
        {
            id,
            name,
            category = "Productivity",
            platforms = new[] { "Windows" },
            assembly = assemblyName
        }));
        return id;
    }

    public static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Build output not found: {source}");

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

}
