using System.Diagnostics;
using System.Threading.Channels;
using Axorith.Contracts;
using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Axorith.Benchmarks;

[MemoryDiagnoser]
public class HostClientBenchmarks
{
    private string _dataPath = null!;
    private string _blockerProbeName = null!;
    private string _blockerProbePath = null!;
    private Guid _appBlockerId;
    private Guid _siteBlockerId;
    private Guid _appBlockerInstanceId;
    private Guid _siteBlockerInstanceId;
    private BenchmarkHostFactory _factory = null!;
    private HttpClient _httpClient = null!;
    private GrpcChannel _channel = null!;
    private PresetsService.PresetsServiceClient _presets = null!;
    private SessionsService.SessionsServiceClient _sessions = null!;
    private ModulesService.ModulesServiceClient _modules = null!;
    private AsyncServerStreamingCall<SettingUpdate> _settingStream = null!;
    private CancellationTokenSource _streamCancellation = null!;
    private Task _streamReader = null!;
    private readonly Channel<SettingUpdate> _updates = Channel.CreateUnbounded<SettingUpdate>();
    private int _updateCounter;
    private string _presetId = null!;
    private bool _sessionStarted;

    static HostClientBenchmarks() =>
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    private async Task SetupAsync()
    {
        BenchmarkEnvironment.Initialize();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("These host and process benchmarks require Windows.");

        _dataPath = Path.Combine(Path.GetTempPath(), "AxorithHostClientBenchmarks", Guid.NewGuid().ToString("N"));
        _blockerProbeName = $"axorith-bench-{Guid.NewGuid():N}";
        _blockerProbePath = Path.Combine(_dataPath, "probe", _blockerProbeName + ".exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_blockerProbePath)!);
        Directory.CreateDirectory(Path.Combine(_dataPath, "empty"));
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), _blockerProbePath);

        var moduleRoot = Path.Combine(_dataPath, "modules");
        _appBlockerId = BenchmarkEnvironment.InstallModule(moduleRoot, typeof(Axorith.Module.AppBlocker.Module), "App Blocker");
        _siteBlockerId = BenchmarkEnvironment.InstallModule(moduleRoot, typeof(Axorith.Module.SiteBlocker.Module), "Site Blocker");
        _factory = new BenchmarkHostFactory(_dataPath);
        _httpClient = _factory.CreateDefaultClient();
        var token = await BenchmarkEnvironment.WaitForAuthTokenAsync(Path.Combine(_dataPath, "config", ".auth_token"));
        _channel = BenchmarkEnvironment.CreateAuthenticatedChannel(_httpClient.BaseAddress!, token, _httpClient);
        _presets = new PresetsService.PresetsServiceClient(_channel);
        _sessions = new SessionsService.SessionsServiceClient(_channel);
        _modules = new ModulesService.ModulesServiceClient(_channel);
        await WaitForModulesAsync();
        await StartBenchmarkSessionAsync();

        _streamCancellation = new CancellationTokenSource();
        _settingStream = _modules.StreamSettingUpdates(
            new StreamSettingUpdatesRequest { ModuleInstanceId = _siteBlockerInstanceId.ToString() },
            cancellationToken: _streamCancellation.Token);
        _streamReader = PumpUpdatesAsync(_streamCancellation.Token);
    }

    [Benchmark]
    public async Task SettingUpdateToClientStream()
    {
        var value = $"sync-{Interlocked.Increment(ref _updateCounter)}.example";
        var response = await _modules.UpdateSettingAsync(new UpdateSettingRequest
        {
            ModuleInstanceId = _siteBlockerInstanceId.ToString(),
            SettingKey = "CustomSites",
            StringValue = value
        });
        if (!response.Success)
            throw new InvalidOperationException(response.Message);

        while (true)
        {
            var update = await _updates.Reader.ReadAsync(_streamCancellation.Token).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));
            if (update.SettingKey == "CustomSites" && update.StringValue == value)
                return;
        }
    }

    [Benchmark]
    public async Task AppBlockerProcessReaction()
    {
        using var process = Process.Start(new ProcessStartInfo(_blockerProbePath, "-t 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the process-blocking probe.");

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (process.ExitCode == 0)
                throw new InvalidOperationException("The App Blocker did not terminate the probe process.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    private async Task CleanupAsync()
    {
        _streamCancellation?.Cancel();
        _settingStream?.Dispose();
        if (_streamReader is not null)
        {
            try { await _streamReader; }
            catch (OperationCanceledException) { }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        }

        if (_sessionStarted)
            await _sessions.StopSessionAsync(new StopSessionRequest());
        _streamCancellation?.Dispose();
        _channel?.Dispose();
        _httpClient?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null!;
        }
        if (Directory.Exists(_dataPath))
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    Directory.Delete(_dataPath, recursive: true);
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt == 19)
                    {
                        // A collectible module context can keep its copied DLL mapped until process exit.
                        break;
                    }

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(25);
                }
            }
        }
    }

    private async Task StartBenchmarkSessionAsync()
    {
        _appBlockerInstanceId = Guid.NewGuid();
        _siteBlockerInstanceId = Guid.NewGuid();
        var preset = new Preset { Name = "Host client benchmark" };
        var appBlocker = new ConfiguredModule
        {
            ModuleId = _appBlockerId.ToString(),
            InstanceId = _appBlockerInstanceId.ToString()
        };
        appBlocker.Settings.Add("Mode", "BlockList");
        appBlocker.Settings.Add("Categories", "[]");
        appBlocker.Settings.Add("CustomProcessList", _blockerProbeName);
        var siteBlocker = new ConfiguredModule
        {
            ModuleId = _siteBlockerId.ToString(),
            InstanceId = _siteBlockerInstanceId.ToString()
        };
        siteBlocker.Settings.Add("Mode", "BlockList");
        siteBlocker.Settings.Add("Categories", "[]");
        siteBlocker.Settings.Add("CustomSites", string.Empty);
        preset.Modules.Add(appBlocker);
        preset.Modules.Add(siteBlocker);

        var created = await _presets.CreatePresetAsync(new CreatePresetRequest { Preset = preset });
        _presetId = created.Id;
        var started = await _sessions.StartSessionAsync(new StartSessionRequest { PresetId = _presetId });
        if (!started.Success)
            throw new InvalidOperationException($"Benchmark session failed to start: {started.Message}");
        _sessionStarted = true;
    }

    private async Task WaitForModulesAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var response = await _modules.ListModulesAsync(new ListModulesRequest());
                if (response.Modules.Any(module => module.Id == _appBlockerId.ToString()) &&
                    response.Modules.Any(module => module.Id == _siteBlockerId.ToString()))
                    return;
            }
            catch (RpcException)
            {
                // The host discovers module assemblies on a background startup task.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The host did not discover the benchmark modules.");
    }

    private async Task PumpUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _settingStream.ResponseStream.MoveNext(cancellationToken))
                await _updates.Writer.WriteAsync(_settingStream.ResponseStream.Current, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
    }

    internal sealed class BenchmarkHostFactory(string dataPath) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.Combine(Directory.GetCurrentDirectory(), "src", "Host"));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:PresetsPath"] = Path.Combine(dataPath, "presets"),
                ["Persistence:LogsPath"] = Path.Combine(dataPath, "logs"),
                ["Persistence:ConfigPath"] = Path.Combine(dataPath, "config"),
                ["Persistence:HostInfoPath"] = Path.Combine(dataPath, "host-info.json"),
                ["Modules:SearchPaths:0"] = Path.Combine(dataPath, "modules"),
                ["Modules:SearchPaths:1"] = Path.Combine(dataPath, "empty"),
                ["Modules:SearchPaths:2"] = Path.Combine(dataPath, "empty"),
                ["Modules:SearchPaths:3"] = Path.Combine(dataPath, "empty"),
                ["Modules:SearchPaths:4"] = Path.Combine(dataPath, "empty")
            }));
        }
    }
}
