using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using Axorith.Client.CoreSdk;
using Axorith.Client.ViewModels;
using Axorith.Contracts;
using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Axorith.Module.Spotify;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;
using Setting = Axorith.Sdk.Settings.Setting;

namespace Axorith.Integrations.Tests;

public sealed class ConfirmedBugRegressionTests
{
    static ConfirmedBugRegressionTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    [Fact]
    public async Task Client_observes_setting_update_errors_when_host_is_unavailable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var channel = GrpcChannel.ForAddress($"http://127.0.0.1:{port}");
        var api = new GrpcModulesApi(
            new ModulesService.ModulesServiceClient(channel),
            Policy.Handle<RpcException>().RetryAsync(0),
            NullLogger.Instance);

        var directFailure = await Assert.ThrowsAsync<RpcException>(() =>
            api.UpdateSettingAsync(Guid.NewGuid(), "enabled", true));
        directFailure.StatusCode.Should().Be(StatusCode.Unavailable);

        var unobservedRpcErrors = new ConcurrentQueue<RpcException>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            foreach (var exception in e.Exception.Flatten().InnerExceptions.OfType<RpcException>())
            {
                unobservedRpcErrors.Enqueue(exception);
            }

            e.SetObserved();
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            using var viewModel = new SettingViewModel(
                Setting.AsCheckbox("enabled", "Enabled", false),
                Guid.NewGuid(),
                Guid.NewGuid(),
                api);

            viewModel.BoolValue = true;
            await Task.Delay(300);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            unobservedRpcErrors.Should().BeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
            api.Dispose();
        }
    }

    [Fact]
    public async Task Client_reactive_task_stream_resolves_its_trace_source_reference()
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = source.Task
            .ToObservable()
            .TakeUntil(Observable.Never<int>())
            .Subscribe(result.SetResult, result.SetException);
        source.SetResult(1);

        (await result.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().Be(1);
    }

    [Fact]
    public async Task Launcher_dispose_terminates_process_before_releasing_its_handle()
    {
        var processErrors = new ConcurrentQueue<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
        {
            foreach (var exception in e.Exception.Flatten().InnerExceptions)
            {
                if (exception is ArgumentNullException { ParamName: "process" } ||
                    exception is InvalidOperationException { Message: "No process is associated with this object." })
                {
                    processErrors.Enqueue(exception);
                }
            }

            e.SetObserved();
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            var settings = new TestLauncherSettings();
            ((ISetting)settings.LifecycleMode).SetValueFromString("KeepRunning");
            var module = new TestLauncherModule(settings);

            for (var i = 0; i < 20; i++)
            {
                typeof(LauncherModuleBase)
                    .GetProperty("CurrentProcess", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(module, Process.GetCurrentProcess());
                module.Dispose();
            }

            await Task.Delay(300);
            settings.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            processErrors.Should().BeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    [Fact]
    public async Task Spotify_dispose_keeps_an_in_flight_refresh_semaphore_releasable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var loggerFactory = LoggerFactory.Create(_ => { });
        var logger = new TestModuleLogger();
        var settings = new Settings(PlatformServices.CreateAppDiscoveryService(loggerFactory));
        var secureStorage = PlatformServices.CreateSecureStorage(NullLogger.Instance);
        var services = new ServiceCollection();
        services.AddHttpClient();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ModuleDefinition { Id = Guid.NewGuid(), Name = "Spotify" };
        var auth = new AuthService(logger, serviceProvider.GetRequiredService<IHttpClientFactory>(), secureStorage,
            definition, settings, new TestNotifier());
        var api = new SpotifyApiService(logger: logger,
            httpClientFactory: serviceProvider.GetRequiredService<IHttpClientFactory>(),
            definition: definition,
            authService: auth);
        var playback = new PlaybackService(logger, settings, auth, api, PlatformServices.CreateProcessService());
        var tokenGate = (SemaphoreSlim)typeof(AuthService)
            .GetField("_tokenRefreshSemaphore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(auth)!;
        await tokenGate.WaitAsync();
        Task refresh;
        try
        {
            refresh = (Task)typeof(PlaybackService)
                .GetMethod("LoadDynamicChoicesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(playback, [true])!;
            playback.Dispose();
        }
        finally
        {
            tokenGate.Release();
        }

        await refresh.WaitAsync(TimeSpan.FromSeconds(3));

        auth.Dispose();
        settings.Dispose();
    }

    [Fact]
    public async Task Shim_exits_when_native_messaging_stdin_reaches_eof()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var solutionRoot = outputDirectory;
        while (solutionRoot != null && !File.Exists(Path.Combine(solutionRoot.FullName, "Axorith.sln")))
        {
            solutionRoot = solutionRoot.Parent;
        }

        solutionRoot.Should().NotBeNull();
        var configuration = outputDirectory.Parent!.Parent!.Name;
        var shimPath = Path.Combine(solutionRoot!.FullName, "build", configuration, "Axorith.Shim", "Axorith.Shim.exe");
        File.Exists(shimPath).Should().BeTrue();

        using var shim = Process.Start(new ProcessStartInfo(shimPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var outputTask = shim.StandardOutput.ReadToEndAsync();
        var errorTask = shim.StandardError.ReadToEndAsync();
        shim.StandardInput.Close();

        try
        {
            await shim.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            shim.Kill(entireProcessTree: true);
            throw;
        }

        shim.ExitCode.Should().Be(0);
        await Task.WhenAll(outputTask, errorTask);
    }

    private sealed class TestLauncherSettings : LauncherSettingsBase
    {
        public override Setting<string> ApplicationPath { get; } = Setting.AsText("path", "Path", string.Empty);

        protected override IEnumerable<ISetting> GetAdditionalSettings() => [];
        protected override IEnumerable<Axorith.Sdk.Actions.IAction> GetAdditionalActions() => [];
    }

    private sealed class TestLauncherModule(TestLauncherSettings settings)
        : LauncherModuleBase(new TestModuleLogger(), PlatformServices.CreateProcessService(),
            PlatformServices.CreateWindowService())
    {
        protected override LauncherSettingsBase Settings => settings;
    }

    private sealed class TestModuleLogger : IModuleLogger
    {
        public void LogDebug(string messageTemplate, params object[] args) { }
        public void LogInfo(string messageTemplate, params object[] args) { }
        public void LogWarning(string messageTemplate, params object[] args) { }
        public void LogError(Exception? exception, string messageTemplate, params object[] args) { }
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) { }
    }

    private sealed class TestNotifier : INotifier
    {
        public void ShowToast(string message, Axorith.Sdk.Services.NotificationType type =
            Axorith.Sdk.Services.NotificationType.Info) { }
        public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) => Task.CompletedTask;
    }
}
