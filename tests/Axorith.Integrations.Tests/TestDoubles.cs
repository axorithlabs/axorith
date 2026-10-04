using Axorith.Contracts;
using Axorith.Sdk.Logging;
using Grpc.Core;
using Grpc.Net.Client;
using Axorith.Sdk.Services;
using NotificationType = Axorith.Sdk.Services.NotificationType;
using System.Runtime.CompilerServices;

namespace Axorith.Integrations.Tests;

internal static class IntegrationTestEnvironment
{
    [ModuleInitializer]
    internal static void ConfigureApplicationPaths()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "AxorithIntegrationTests", Environment.ProcessId.ToString());
        Environment.SetEnvironmentVariable("AXORITH_ROAMING_ROOT", testRoot);
        Environment.SetEnvironmentVariable("AXORITH_LOCAL_ROOT", Path.Combine(testRoot, "local"));
        Environment.SetEnvironmentVariable("AXORITH_TEST_MODE", "1");
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
