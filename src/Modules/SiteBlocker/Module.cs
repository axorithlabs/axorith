using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SiteBlocker;

/// <summary>
///     A module that blocks websites by sending commands to the Axorith.Shim process
///     via a Named Pipe, which then relays them to a browser extension.
/// </summary>
public class Module(IModuleLogger logger, INotifier notifier) : IModule
{
    private readonly Setting<string> _mode = Setting.AsChoice(
        key: "Mode",
        label: "Blocking Mode",
        defaultValue: "BlockList",
        initialChoices:
        [
            new KeyValuePair<string, string>("BlockList", "Block List (Blacklist)"),
            new KeyValuePair<string, string>("AllowList", "Allow List (Whitelist)")
        ],
        description: "BlockList: Blocks listed sites. AllowList: Blocks EVERYTHING except listed sites."
    );

    private readonly Setting<string> _siteList = Setting.AsTextArea(
        key: "BlockedSites",
        label: "Site List",
        description:
        "A comma-separated list of domains (e.g., youtube.com, twitter.com). Behavior depends on Mode.",
        defaultValue: "youtube.com, twitter.com, reddit.com"
    );

    private readonly string _pipeName = "axorith-nm-pipe";

    private List<string> _activeSiteList = [];

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return [_mode, _siteList];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        var installAction = Action.Create("InstallFirefoxExtension", "Install Firefox Extension");
        installAction.OnInvokeAsync(OpenFirefoxExtensionPageAsync);
        return [installAction];
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        var sites = _siteList.GetCurrentValue();
        return Task.FromResult(string.IsNullOrWhiteSpace(sites)
            ? ValidationResult.Warn("Site list is empty. No action will be taken.")
            : ValidationResult.Success);
    }

    /// <inheritdoc />
    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var mode = _mode.GetCurrentValue();
        logger.LogInfo("Sending 'block' command via Named Pipe (Mode: {Mode})...", mode);

        _activeSiteList =
        [
            .. _siteList.GetCurrentValue().Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
        ];

        if (_activeSiteList.Count == 0)
        {
            logger.LogWarning("No sites specified. Module will do nothing.");
            return Task.CompletedTask;
        }

        logger.LogDebug("Sites: {Sites}", string.Join(", ", _activeSiteList));

        var message = new
        {
            command = "block",
            mode,
            sites = _activeSiteList
        };

        return WriteToPipeAsync(message);
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInfo("Sending 'unblock' command via Named Pipe...");

        if (_activeSiteList.Count == 0)
        {
            return Task.CompletedTask;
        }

        var message = new { command = "unblock" };
        var resultTask = WriteToPipeAsync(message);
        _activeSiteList.Clear();
        return resultTask;
    }

    private async Task WriteToPipeAsync(object message)
    {
        try
        {
            await using var pipeClient =
                new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync(2000);

            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);

            await pipeClient.WriteAsync(buffer, 0, buffer.Length);
            await pipeClient.FlushAsync();

            var commandName = message.GetType().GetProperty("command")?.GetValue(message) ?? "unknown";
            logger.LogInfo("Command '{Command}' sent successfully via Named Pipe.", commandName);
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex,
                "Could not connect to the Axorith Shim process via Named Pipe. Is the browser extension installed and running?");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send command to Shim via Named Pipe.");
        }
    }

    /// <summary>
    ///     Opens the Firefox extension installation page in the default browser
    /// </summary>
    private async Task OpenFirefoxExtensionPageAsync()
    {
        const string firefoxExtensionUrl = "https://addons.mozilla.org/firefox/addon/axorith-site-blocker/";

        try
        {
            Process.Start(new ProcessStartInfo(firefoxExtensionUrl) { UseShellExecute = true });

            notifier.ShowToast("Firefox extension page opened in your browser", NotificationType.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open Firefox extension page in browser");
            notifier.ShowToast("Failed to open browser. Please manually visit: " + firefoxExtensionUrl,
                NotificationType.Error);
        }

        // Small delay to ensure the operation completes
        await Task.Delay(100);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_activeSiteList.Count <= 0)
        {
            return;
        }

        logger.LogWarning(
            "Disposing module while sites are still blocked. Attempting to send final unblock command.");
        var message = new { command = "unblock" };

        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var task = WriteToPipeAsync(message);
                    if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) != task)
                    {
                        logger.LogWarning("Unblock command timed out during disposal");
                    }
                }
                catch (Exception)
                {
                    logger.LogWarning("Failed to send unblock command during disposal");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send unblock command during disposal");
        }
        finally
        {
            _activeSiteList.Clear();
        }
    }
}