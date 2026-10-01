using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Axorith.Contracts;
using Axorith.Shared.Platform.Windows;
using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Benchmarks;

public class ClientColdStartBenchmarks
{
    private const string HostMutexName = "Global\\AxorithHostInstanceMutex";
    private const string ReadyMessage = "Axorith Client initialization sequence complete.";
    private readonly object _outputLock = new();
    private readonly StringBuilder _clientOutput = new();
    private string _installDirectory = null!;
    private string _clientDirectory = null!;
    private string _hostDirectory = null!;
    private string _modulesDirectory = null!;
    private string _profileDirectory = null!;
    private string _roamingRoot = null!;
    private string _localRoot = null!;
    private string _configDirectory = null!;
    private string _logsDirectory = null!;
    private string _hostInfoPath = null!;
    private string _tokenPath = null!;
    private string _clientLogDirectory = null!;
    private int _hostPort;
    private Process? _hostProcess;
    private Process? _clientProcess;
    private Task<string>? _hostStdout;
    private Task<string>? _hostStderr;
    private GrpcChannel? _hostChannel;
    private HostManagement.HostManagementClient? _hostManagement;

    static ClientColdStartBenchmarks() =>
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            SetupAsync().GetAwaiter().GetResult();
        }
        catch
        {
            CleanupHostAsync().GetAwaiter().GetResult();
            throw;
        }
    }

    private async Task SetupAsync()
    {
        BenchmarkEnvironment.Initialize();
        EnsureNoRunningClientOrHost();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A real Client process and visible window require Windows.");

        var buildDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        _installDirectory = Path.Combine(BenchmarkEnvironment.Root, "client-installation");
        _clientDirectory = Path.Combine(_installDirectory, "Axorith.Client");
        _hostDirectory = Path.Combine(_installDirectory, "Axorith.Host");
        _modulesDirectory = Path.Combine(_installDirectory, "Modules");
        CopyDirectory(Path.Combine(buildDirectory, "Axorith.Client"), _clientDirectory);
        CopyDirectory(Path.Combine(buildDirectory, "Axorith.Host"), _hostDirectory);
        CopyDirectory(Path.Combine(buildDirectory, "Modules"), _modulesDirectory);

        _profileDirectory = Path.Combine(BenchmarkEnvironment.Root, "client-profile", Guid.NewGuid().ToString("N"));
        _roamingRoot = Path.Combine(_profileDirectory, "AppData", "Roaming", "Axorith");
        _localRoot = Path.Combine(_profileDirectory, "AppData", "Local", "Axorith");
        _configDirectory = Path.Combine(_roamingRoot, "config");
        _logsDirectory = Path.Combine(_roamingRoot, "logs");
        _hostInfoPath = Path.Combine(_roamingRoot, "host-info.json");
        _tokenPath = Path.Combine(_configDirectory, ".auth_token");
        _clientLogDirectory = _logsDirectory;
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_logsDirectory);
        Directory.CreateDirectory(_localRoot);
        File.WriteAllText(Path.Combine(_configDirectory, "telemetry-preference.txt"), "false");

        var autostartEnabled = OperatingSystem.IsWindows() &&
                               new WindowsAutoStartManager(NullLogger.Instance).IsAutoStartEnabled;
        File.WriteAllText(Path.Combine(_clientDirectory, "clientsettings.json"), JsonSerializer.Serialize(new
        {
            AutoStartEnabled = autostartEnabled,
            AutoStartMinimized = true,
            TelemetryEnabled = false,
            MinimizeToTrayOnClose = false
        }));

        _hostPort = GetAvailablePort();
        var hostStartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = _hostDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        hostStartInfo.ArgumentList.Add(Path.Combine(_hostDirectory, "Axorith.Host.dll"));
        hostStartInfo.ArgumentList.Add("--Grpc:BindAddress=127.0.0.1");
        hostStartInfo.ArgumentList.Add($"--Grpc:Port={_hostPort}");
        hostStartInfo.ArgumentList.Add($"--Persistence:ConfigPath={_configDirectory}");
        hostStartInfo.ArgumentList.Add($"--Persistence:HostInfoPath={_hostInfoPath}");
        hostStartInfo.ArgumentList.Add($"--Persistence:LogsPath={_logsDirectory}");
        hostStartInfo.ArgumentList.Add($"--Persistence:PresetsPath={Path.Combine(_roamingRoot, "presets")}");
        hostStartInfo.ArgumentList.Add($"--Modules:SearchPaths:0={_modulesDirectory}");
        hostStartInfo.ArgumentList.Add("--Logging:LogLevel:Default=Warning");
        ConfigureProfileEnvironment(hostStartInfo.Environment);
        hostStartInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        hostStartInfo.Environment["DOTNET_NOLOGO"] = "1";
        hostStartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        _hostProcess = Process.Start(hostStartInfo)
                       ?? throw new InvalidOperationException("Could not start the benchmark Host process.");
        _hostStdout = _hostProcess.StandardOutput.ReadToEndAsync();
        _hostStderr = _hostProcess.StandardError.ReadToEndAsync();
        await ConnectToHealthyHostAsync();
    }

    [Benchmark]
    [InvocationCount(1)]
    public async Task ClientColdStartToConnectedMainWindow()
    {
        lock (_outputLock)
            _clientOutput.Clear();

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startInfo = new ProcessStartInfo(Path.Combine(_clientDirectory, "Axorith.Client.exe"))
        {
            WorkingDirectory = _clientDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ConfigureProfileEnvironment(startInfo.Environment);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.ArgumentList.Add($"--Host:Port={_hostPort}");

        _clientProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _clientProcess.OutputDataReceived += (_, eventArgs) => CaptureClientOutput(eventArgs.Data, ready);
        _clientProcess.ErrorDataReceived += (_, eventArgs) => CaptureClientOutput(eventArgs.Data, ready);

        var timer = Stopwatch.StartNew();
        if (!_clientProcess.Start())
            throw new InvalidOperationException("Could not start the real Axorith.Client process.");
        _clientProcess.BeginOutputReadLine();
        _clientProcess.BeginErrorReadLine();

        while (timer.Elapsed < TimeSpan.FromSeconds(45))
        {
            if (_clientProcess.HasExited)
            {
                throw new InvalidOperationException(
                    $"Axorith.Client exited with code {_clientProcess.ExitCode} before becoming ready.\n{GetClientOutput()}");
            }

            if (ready.Task.IsCompleted && HasVisibleMainWindow(_clientProcess.Id))
                return;

            if (IsReadyInLog() && HasVisibleMainWindow(_clientProcess.Id))
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException(
            $"Axorith.Client did not connect and show its main window within 45 seconds.\n{GetClientOutput()}");
    }

    [IterationCleanup(Target = nameof(ClientColdStartToConnectedMainWindow))]
    public void CleanupClient()
    {
        if (_clientProcess is { HasExited: false })
        {
            _clientProcess.Kill(entireProcessTree: true);
            _clientProcess.WaitForExit();
        }

        _clientProcess?.Dispose();
        _clientProcess = null;
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupHostAsync().GetAwaiter().GetResult();

    private async Task CleanupHostAsync()
    {
        CleanupClient();
        Exception? shutdownFailure = null;
        if (_hostProcess is { HasExited: false } && _hostManagement is not null)
        {
            try
            {
                var response = await _hostManagement.RequestShutdownAsync(new ShutdownRequest
                {
                    Reason = "Client cold-start benchmark complete"
                }).ResponseAsync.WaitAsync(TimeSpan.FromSeconds(5));
                if (!response.Accepted)
                    throw new InvalidOperationException(response.Message);
            }
            catch (Exception ex)
            {
                shutdownFailure = ex;
            }
        }

        if (_hostProcess is { HasExited: false })
        {
            try
            {
                await _hostProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException ex)
            {
                _hostProcess.Kill(entireProcessTree: true);
                await _hostProcess.WaitForExitAsync();
                shutdownFailure ??= ex;
            }
        }

        _hostChannel?.Dispose();
        _hostChannel = null;
        var stdout = _hostStdout is null ? string.Empty : await _hostStdout;
        var stderr = _hostStderr is null ? string.Empty : await _hostStderr;
        _hostProcess?.Dispose();
        _hostProcess = null;
        _hostManagement = null;

        if (shutdownFailure is not null)
            throw new InvalidOperationException($"Benchmark Host shutdown failed.\n{shutdownFailure}\n{stdout}\n{stderr}", shutdownFailure);

        if (Directory.Exists(_installDirectory))
            Directory.Delete(_installDirectory, recursive: true);
        if (Directory.Exists(_profileDirectory))
            Directory.Delete(_profileDirectory, recursive: true);
    }

    private async Task ConnectToHealthyHostAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(45))
        {
            if (_hostProcess?.HasExited == true)
            {
                var stdout = _hostStdout is null ? string.Empty : await _hostStdout;
                var stderr = _hostStderr is null ? string.Empty : await _hostStderr;
                throw new InvalidOperationException(
                    $"Benchmark Host exited with code {_hostProcess.ExitCode}.\n{stdout}\n{stderr}");
            }

            if (File.Exists(_hostInfoPath) && File.Exists(_tokenPath))
            {
                try
                {
                    using var info = JsonDocument.Parse(await File.ReadAllTextAsync(_hostInfoPath));
                    var port = info.RootElement.GetProperty("port").GetInt32();
                    var token = await File.ReadAllTextAsync(_tokenPath);
                    var credentials = CallCredentials.FromInterceptor((_, metadata) =>
                    {
                        metadata.Add("x-axorith-auth-token", token);
                        return Task.CompletedTask;
                    });
                    _hostChannel = GrpcChannel.ForAddress($"http://127.0.0.1:{port}", new GrpcChannelOptions
                    {
                        Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials),
                        UnsafeUseInsecureChannelCallCredentials = true
                    });

                    var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(_hostChannel);
                    var health = await diagnostics.GetHealthAsync(new HealthCheckRequest(),
                        deadline: DateTime.UtcNow.AddSeconds(2));
                    if (health.Status == HealthStatus.Healthy && health.LoadedModules >= 4)
                    {
                        _hostManagement = new HostManagement.HostManagementClient(_hostChannel);
                        return;
                    }

                    _hostChannel.Dispose();
                    _hostChannel = null;
                }
                catch (Exception ex) when (ex is IOException or JsonException or RpcException or KeyNotFoundException)
                {
                    _hostChannel?.Dispose();
                    _hostChannel = null;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The real Host did not become healthy with all four modules.");
    }

    private bool IsReadyInLog()
    {
        try
        {
            return Directory.Exists(_clientLogDirectory) &&
                   Directory.EnumerateFiles(_clientLogDirectory, "client-*.log")
                       .Any(path => File.ReadAllText(path).Contains(ReadyMessage, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void CaptureClientOutput(string? line, TaskCompletionSource ready)
    {
        if (line is null)
            return;

        lock (_outputLock)
            _clientOutput.AppendLine(line);
        if (line.Contains(ReadyMessage, StringComparison.Ordinal))
            ready.TrySetResult();
    }

    private string GetClientOutput()
    {
        lock (_outputLock)
            return _clientOutput.ToString();
    }

    private void ConfigureProfileEnvironment(IDictionary<string, string?> environment)
    {
        environment["AXORITH_ROAMING_ROOT"] = _roamingRoot;
        environment["AXORITH_LOCAL_ROOT"] = _localRoot;
        environment["APPDATA"] = Path.GetDirectoryName(_roamingRoot)!;
        environment["LOCALAPPDATA"] = Path.GetDirectoryName(_localRoot)!;
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool HasVisibleMainWindow(int processId)
    {
        var visible = false;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
                return true;

            GetWindowThreadProcessId(window, out var ownerProcessId);
            if (ownerProcessId != (uint)processId)
                return true;

            var title = new StringBuilder(256);
            _ = GetWindowText(window, title, title.Capacity);
            if (title.ToString() == "Axorith")
            {
                visible = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return visible;
    }

    private static void EnsureNoRunningClientOrHost()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A real Client process and visible window require Windows.");

        var clients = Process.GetProcessesByName("Axorith.Client");
        try
        {
            if (clients.Length > 0)
                throw new InvalidOperationException("Close Axorith.Client before measuring its cold start.");
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }

        try
        {
            using var mutex = Mutex.OpenExisting(HostMutexName);
            throw new InvalidOperationException("Stop the running Axorith.Host before measuring Client startup.");
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No Host process currently owns the global singleton mutex.
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Build output not found: {sourceDirectory}");

        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);
}
