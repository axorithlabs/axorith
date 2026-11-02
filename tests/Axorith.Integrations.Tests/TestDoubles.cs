using System.Runtime.CompilerServices;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationType = Axorith.Sdk.Services.NotificationType;

namespace Axorith.Integrations.Tests;

public class IsolatedHostFactory : WebApplicationFactory<Program>
{
    private static readonly object StartupLock = new();
    private readonly string _mutexName = "Local\\AxorithTestHost-" + Guid.NewGuid().ToString("N");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Replace the external Windows policy boundary, never the session manager or modules.
        builder.ConfigureServices(services => services.AddSingleton<ICommitmentProtectionService, TestCommitmentProtection>());
        // Host singleton detection must be isolated as well as its files and HTTP server.
        lock (StartupLock)
        {
            var previous = Environment.GetEnvironmentVariable("AXORITH_TEST_HOST_MUTEX");
            try
            {
                Environment.SetEnvironmentVariable("AXORITH_TEST_HOST_MUTEX", _mutexName);
                return base.CreateHost(builder);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AXORITH_TEST_HOST_MUTEX", previous);
            }
        }
    }
}

internal sealed class TestCommitmentProtection : ICommitmentProtectionService
{
    private bool _enabled;
    public Task CheckCanEnableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CheckRecoveryStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CheckCanSetRecoveryStartupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetRecoveryStartupAsync(bool active, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnableAsync(CancellationToken cancellationToken = default) { _enabled = true; return Task.CompletedTask; }
    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(_enabled);
    public Task RestoreAsync(CancellationToken cancellationToken = default) { _enabled = false; return Task.CompletedTask; }
    public Task ReconcileAsync(bool strictSessionActive, CancellationToken cancellationToken = default) =>
        strictSessionActive ? EnableAsync(cancellationToken) : RestoreAsync(cancellationToken);
}

internal static class IntegrationTestEnvironment
{
    [ModuleInitializer]
    internal static void ConfigureApplicationPaths()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AxorithIntegrationTests", Environment.ProcessId.ToString());
        Environment.SetEnvironmentVariable("AXORITH_ROAMING_ROOT", testRoot);
        Environment.SetEnvironmentVariable("AXORITH_LOCAL_ROOT", Path.Combine(testRoot, "local"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}

internal sealed class TestModuleLogger : IModuleLogger
{
    public void LogDebug(string messageTemplate, params object[] args) { }
    public void LogInfo(string messageTemplate, params object[] args) { }
    public void LogWarning(string messageTemplate, params object[] args) { }
    public void LogError(Exception? exception, string messageTemplate, params object[] args) { }
    public void LogFatal(Exception? exception, string messageTemplate, params object[] args) { }
}

internal sealed class NoopNotifier : INotifier
{
    public void ShowToast(string message, NotificationType type = NotificationType.Info) { }
    public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) => Task.CompletedTask;
}

internal sealed class EmptySecureStorage : ISecureStorageService
{
    public void StoreSecret(string key, string secret) { }
    public string? RetrieveSecret(string key) => null;
    public void DeleteSecret(string key) { }
}

internal static class TestGrpc
{
    public static GrpcChannel CreateAuthenticatedChannel(HttpClient client, string token)
    {
        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            metadata.Add(AuthConstants.TokenHeaderName, token);
            return Task.CompletedTask;
        });

        return GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = client,
            Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials),
            UnsafeUseInsecureChannelCallCredentials = true
        });
    }
}
