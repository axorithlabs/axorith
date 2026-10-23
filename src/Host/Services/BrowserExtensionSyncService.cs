using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Platform;

namespace Axorith.Host.Services;

public sealed class BrowserExtensionSyncService(ISessionManager sessions, ILoggerFactory loggerFactory,
    ILogger<BrowserExtensionSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = PlatformServices.CreateNamedPipeFactory(loggerFactory);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = factory.CreateSecureServerPipe("axorith-browser-state", PipeDirection.Out);
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                var session = sessions.ActiveSession;
                var rules = session?.Modules
                    .Select(module => sessions.GetActiveModuleInstanceByInstanceId(module.InstanceId))
                    .OfType<IBrowserBlockingState>()
                    .Select(module => module.BrowserRules)
                    .OfType<BrowserBlockingRules>().ToArray() ?? [];
                var response = JsonSerializer.Serialize(new
                {
                    command = "sync",
                    rules = rules.Select(rule => new { mode = rule.Mode, sites = rule.Sites }),
                    retainOnDisconnect = session?.FocusCommitment.IsCommitted == true
                });
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await writer.WriteLineAsync(response.AsMemory(), timeout.Token).ConfigureAwait(false);
                await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Browser state synchronization failed.");
                try { await Task.Delay(500, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
