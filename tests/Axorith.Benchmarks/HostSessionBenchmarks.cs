using Axorith.Contracts;
using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Net.Client;

namespace Axorith.Benchmarks;

[MemoryDiagnoser]
public class HostSessionBenchmarks
{
    private string _dataPath = null!;
    private Guid _appBlockerId;
    private string _presetId = null!;
    private HostClientBenchmarks.BenchmarkHostFactory _factory = null!;
    private HttpClient _httpClient = null!;
    private GrpcChannel _channel = null!;
    private PresetsService.PresetsServiceClient _presets = null!;
    private SessionsService.SessionsServiceClient _sessions = null!;
    private bool _sessionStarted;

    static HostSessionBenchmarks() =>
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    private async Task SetupAsync()
    {
        BenchmarkEnvironment.Initialize();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The real AppBlocker session benchmark requires Windows.");

        _dataPath = Path.Combine(BenchmarkEnvironment.Root, "session-start", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dataPath, "empty"));
        _appBlockerId = HostClientBenchmarks.InstallModule(Path.Combine(_dataPath, "modules"),
            typeof(Axorith.Module.AppBlocker.Module), "App Blocker");

        _factory = new HostClientBenchmarks.BenchmarkHostFactory(_dataPath);
        _httpClient = _factory.CreateDefaultClient();
        var tokenPath = Path.Combine(_dataPath, "config", ".auth_token");
        var token = await HostClientBenchmarks.WaitForAuthTokenAsync(tokenPath);
        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            metadata.Add("x-axorith-auth-token", token);
            return Task.CompletedTask;
        });
        _channel = GrpcChannel.ForAddress(_httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = _httpClient,
            Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials),
            UnsafeUseInsecureChannelCallCredentials = true
        });
        _presets = new PresetsService.PresetsServiceClient(_channel);
        _sessions = new SessionsService.SessionsServiceClient(_channel);
        await WaitForAppBlockerAsync();

        var module = new ConfiguredModule
        {
            ModuleId = _appBlockerId.ToString(),
            InstanceId = Guid.NewGuid().ToString()
        };
        module.Settings.Add("Mode", "BlockList");
        module.Settings.Add("Categories", "[]");
        module.Settings.Add("CustomProcessList", "axorith-benchmark-no-such-process");
        var created = await _presets.CreatePresetAsync(new CreatePresetRequest
        {
            Preset = new Preset { Name = "AppBlocker session benchmark", Modules = { module } }
        });
        _presetId = created.Id;
    }

    [Benchmark]
    public async Task AppBlockerSessionStartAndStop()
    {
        var started = await _sessions.StartSessionAsync(new StartSessionRequest { PresetId = _presetId });
        if (!started.Success)
            throw new InvalidOperationException($"AppBlocker session did not start: {started.Message}");

        _sessionStarted = true;
        var stopped = await _sessions.StopSessionAsync(new StopSessionRequest());
        if (!stopped.Success)
            throw new InvalidOperationException($"AppBlocker session did not stop: {stopped.Message}");

        _sessionStarted = false;
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    private async Task CleanupAsync()
    {
        if (_sessionStarted)
        {
            try { await _sessions.StopSessionAsync(new StopSessionRequest()); }
            catch { }
        }

        _channel?.Dispose();
        _httpClient?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null!;
        }

        if (Directory.Exists(_dataPath))
        {
            try { Directory.Delete(_dataPath, recursive: true); }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task WaitForAppBlockerAsync()
    {
        var modules = new ModulesService.ModulesServiceClient(_channel);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var response = await modules.ListModulesAsync(new ListModulesRequest());
                if (response.Modules.Any(module => module.Id == _appBlockerId.ToString()))
                    return;
            }
            catch (RpcException)
            {
                // Host module discovery finishes asynchronously during startup.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Host did not discover the real AppBlocker module.");
    }
}
