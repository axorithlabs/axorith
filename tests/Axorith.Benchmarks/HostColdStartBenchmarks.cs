using System.Diagnostics;
using System.Text.Json;
using Axorith.Contracts;
using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Net.Client;

namespace Axorith.Benchmarks;

public class HostColdStartBenchmarks
{
    private const string HostMutexName = "Global\\AxorithHostInstanceMutex";
    private readonly Dictionary<string, Guid> _moduleIds = new(StringComparer.Ordinal);
    private string _installationDirectory = null!;
    private string _hostDirectory = null!;
    private string _moduleDirectory = null!;
    private string _iterationDirectory = null!;
    private string _hostInfoPath = null!;
    private string _tokenPath = null!;
    private ProcessStartInfo _startInfo = null!;
    private Process? _hostProcess;
    private Task<string>? _standardOutput;
    private Task<string>? _standardError;
    private GrpcChannel? _channel;
    private ModulesService.ModulesServiceClient? _modules;
    private HostManagement.HostManagementClient? _management;

    static HostColdStartBenchmarks() =>
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    private Task SetupAsync()
    {
        BenchmarkEnvironment.Initialize();
        EnsureWindowsAndNoRunningHost();

        var buildDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        var hostOutput = Path.Combine(buildDirectory, "Axorith.Host");
        if (!File.Exists(Path.Combine(hostOutput, "Axorith.Host.dll")))
            throw new FileNotFoundException("Build Axorith.Host before running its cold-start benchmark.", hostOutput);

        _installationDirectory = Path.Combine(BenchmarkEnvironment.Root, "host-installation");
        _hostDirectory = Path.Combine(_installationDirectory, "Axorith.Host");
        _moduleDirectory = Path.Combine(_installationDirectory, "Modules");
        BenchmarkEnvironment.CopyDirectory(hostOutput, _hostDirectory);

        InstallModule(typeof(Axorith.Module.AppBlocker.Module), "AppBlocker");
        InstallModule(typeof(Axorith.Module.ApplicationLauncher.Module), "ApplicationLauncher");
        InstallModule(typeof(Axorith.Module.HomeAssistant.Module), "HomeAssistant");
        InstallModule(typeof(Axorith.Module.SiteBlocker.Module), "SiteBlocker");

        return Task.CompletedTask;
    }

    [IterationSetup(Target = nameof(HostProcessColdStartToLoadedModuleCatalog))]
    public void PrepareIteration()
    {
        EnsureWindowsAndNoRunningHost();

        _iterationDirectory = Path.Combine(BenchmarkEnvironment.Root, "host-cold-start",
            Guid.NewGuid().ToString("N"));
        var roamingRoot = Path.Combine(_iterationDirectory, "roaming", "Axorith");
        var localRoot = Path.Combine(_iterationDirectory, "local", "Axorith");
        var configDirectory = Path.Combine(_iterationDirectory, "config");
        var roamingConfigDirectory = Path.Combine(roamingRoot, "config");
        var presetsDirectory = Path.Combine(_iterationDirectory, "presets");
        var logsDirectory = Path.Combine(_iterationDirectory, "logs");
        _hostInfoPath = Path.Combine(roamingRoot, "host-info.json");
        _tokenPath = Path.Combine(configDirectory, ".auth_token");

        Directory.CreateDirectory(roamingRoot);
        Directory.CreateDirectory(localRoot);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(roamingConfigDirectory);
        Directory.CreateDirectory(presetsDirectory);
        Directory.CreateDirectory(logsDirectory);
        File.WriteAllText(Path.Combine(roamingConfigDirectory, "telemetry-preference.txt"), "false");

        _startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = _hostDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        _startInfo.ArgumentList.Add(Path.Combine(_hostDirectory, "Axorith.Host.dll"));
        _startInfo.ArgumentList.Add("--Grpc:BindAddress=127.0.0.1");
        _startInfo.ArgumentList.Add("--Grpc:Port=0");
        _startInfo.ArgumentList.Add($"--Persistence:ConfigPath={configDirectory}");
        _startInfo.ArgumentList.Add($"--Persistence:HostInfoPath={_hostInfoPath}");
        _startInfo.ArgumentList.Add($"--Persistence:LogsPath={logsDirectory}");
        _startInfo.ArgumentList.Add($"--Persistence:PresetsPath={presetsDirectory}");
        _startInfo.ArgumentList.Add($"--Modules:SearchPaths:0={_moduleDirectory}");
        _startInfo.ArgumentList.Add("--Logging:LogLevel:Default=Warning");
        _startInfo.Environment["AXORITH_ROAMING_ROOT"] = roamingRoot;
        _startInfo.Environment["AXORITH_LOCAL_ROOT"] = localRoot;
        _startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        _startInfo.Environment["DOTNET_NOLOGO"] = "1";
        _startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    }

    [Benchmark]
    public async Task HostProcessColdStartToLoadedModuleCatalog()
    {
        _hostProcess = Process.Start(_startInfo)
                       ?? throw new InvalidOperationException("Could not start Axorith.Host.dll.");
        _standardOutput = _hostProcess.StandardOutput.ReadToEndAsync();
        _standardError = _hostProcess.StandardError.ReadToEndAsync();

        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(45))
        {
            if (_hostProcess.HasExited)
            {
                var output = await _standardOutput;
                var error = await _standardError;
                throw new InvalidOperationException(
                    $"Axorith.Host exited with code {_hostProcess.ExitCode} before becoming ready.\n{output}\n{error}");
            }

            if (_channel is null && File.Exists(_hostInfoPath))
                await ConnectToHostAsync();

            if (_modules is not null)
            {
                using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    var response = await _modules.ListModulesAsync(new ListModulesRequest(),
                        cancellationToken: requestTimeout.Token);
                    var loadedIds = response.Modules.Select(module => Guid.Parse(module.Id)).ToHashSet();
                    if (_moduleIds.Values.All(loadedIds.Contains))
                        return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
                {
                    // The child process is still binding its gRPC listener.
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Axorith.Host did not expose all four real modules within 45 seconds.");
    }

    [IterationCleanup(Target = nameof(HostProcessColdStartToLoadedModuleCatalog))]
    public void CleanupIteration() => CleanupIterationAsync().GetAwaiter().GetResult();

    private async Task CleanupIterationAsync()
    {
        Exception? shutdownFailure = null;
        if (_hostProcess is { HasExited: false } && _management is not null)
        {
            try
            {
                var response = await _management.RequestShutdownAsync(new ShutdownRequest
                {
                    Reason = "Benchmark iteration complete"
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

        _channel?.Dispose();
        _channel = null;
        _modules = null;
        _management = null;
        var output = _standardOutput is null ? string.Empty : await _standardOutput;
        var error = _standardError is null ? string.Empty : await _standardError;
        _hostProcess?.Dispose();
        _hostProcess = null;
        _standardOutput = null;
        _standardError = null;

        if (Directory.Exists(_iterationDirectory))
            Directory.Delete(_iterationDirectory, recursive: true);

        if (shutdownFailure is not null)
        {
            throw new InvalidOperationException(
                $"The host did not shut down through its authenticated gRPC request.\n{shutdownFailure}\n{output}\n{error}",
                shutdownFailure);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupGlobalAsync().GetAwaiter().GetResult();

    private async Task CleanupGlobalAsync()
    {
        await CleanupIterationAsync();
        if (Directory.Exists(_installationDirectory))
            Directory.Delete(_installationDirectory, recursive: true);
    }

    private async Task ConnectToHostAsync()
    {
        if (!File.Exists(_tokenPath))
            return;

        int port;
        string token;
        try
        {
            using var hostInfo = JsonDocument.Parse(await File.ReadAllTextAsync(_hostInfoPath));
            port = hostInfo.RootElement.GetProperty("port").GetInt32();
            token = await File.ReadAllTextAsync(_tokenPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            return;
        }

        _channel = BenchmarkEnvironment.CreateAuthenticatedChannel(new Uri($"http://127.0.0.1:{port}"), token);
        _modules = new ModulesService.ModulesServiceClient(_channel);
        _management = new HostManagement.HostManagementClient(_channel);
    }

    private void InstallModule(Type moduleType, string name)
    {
        var id = Guid.NewGuid();
        var moduleDirectory = Path.Combine(_moduleDirectory, name);
        var sourceDirectory = Path.GetDirectoryName(moduleType.Assembly.Location)!;
        BenchmarkEnvironment.CopyDirectory(sourceDirectory, moduleDirectory);

        var assemblyName = Path.GetFileName(moduleType.Assembly.Location);
        File.WriteAllText(Path.Combine(moduleDirectory, "module.json"), JsonSerializer.Serialize(new
        {
            id,
            name,
            category = "Productivity",
            platforms = new[] { "Windows" },
            assembly = assemblyName
        }));
        _moduleIds.Add(name, id);
    }

    private static void EnsureWindowsAndNoRunningHost()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The real Host process startup benchmark requires Windows.");

        var clients = Process.GetProcessesByName("Axorith.Client");
        try
        {
            if (clients.Length > 0)
                throw new InvalidOperationException("Close Axorith.Client before measuring Host process startup.");
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }

        try
        {
            using var hostMutex = Mutex.OpenExisting(HostMutexName);
            throw new InvalidOperationException("Stop the running Axorith.Host before measuring a cold start.");
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No Host process currently owns the global singleton mutex.
        }
    }
}
