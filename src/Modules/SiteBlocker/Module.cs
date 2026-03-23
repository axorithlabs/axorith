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

public class Module(IModuleLogger logger, INotifier notifier) : IModule
{
    private static Dictionary<string, string[]>? _categorySites;
    private static readonly Lock LoadLock = new();

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

    private readonly Setting<List<string>> _categories = Setting.AsMultiChoice(
        key: "Categories",
        label: "Block Categories",
        defaultValues:
        ["Social", "Video", "Streaming", "Gaming", "News", "Shopping", "Adult", "Gambling", "Dating", "Forums"],
        initialChoices: GetCategoryChoices(),
        description: "Select categories to block. Sites from selected categories will be automatically added."
    );

    private readonly Setting<string> _customSites = Setting.AsTextArea(
        key: "CustomSites",
        label: "Custom Sites",
        description: "Additional domains to block/allow (comma or newline separated). Example: example.com, test.org",
        defaultValue: ""
    );

    private readonly string _pipeName = "axorith-nm-pipe";
    private List<string> _activeSiteList = [];
    private bool _disposed;

    public IReadOnlyList<ISetting> GetSettings()
    {
        return [_mode, _categories, _customSites];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        var installFirefoxAction = Action.Create("InstallFirefoxExtension", "Install Firefox Extension");
        installFirefoxAction.OnInvokeAsync(OpenFirefoxExtensionPageAsync);

        var installChromeAction = Action.Create("InstallChromeExtension", "Install Chrome Extension", false);
        installChromeAction.OnInvokeAsync(OpenChromeExtensionPageAsync);

        return [installFirefoxAction, installChromeAction];
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        var categories = _categories.GetCurrentValue();
        var custom = _customSites.GetCurrentValue();

        if (categories.Count == 0 && string.IsNullOrWhiteSpace(custom))
        {
            return Task.FromResult(
                ValidationResult.Warn("No categories or sites selected. The module will not block anything."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var mode = _mode.GetCurrentValue();
        logger.LogInfo("Sending 'block' command via Named Pipe (Mode: {Mode})...", mode);

        _activeSiteList = GetAllSites();

        if (_activeSiteList.Count == 0)
        {
            logger.LogWarning("No sites specified. Module will do nothing.");
            return Task.CompletedTask;
        }

        logger.LogDebug("Blocking {Count} sites", _activeSiteList.Count);

        var message = new { command = "block", mode, sites = _activeSiteList };
        return WriteToPipeAsync(message);
    }

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

    private List<string> GetAllSites()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in _categories.GetCurrentValue())
        {
            if (_categorySites!.TryGetValue(cat, out var sites))
            {
                foreach (var site in sites)
                {
                    result.Add(site);
                }
            }
        }

        var custom = _customSites.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            foreach (var site in custom.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = site.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return [.. result];
    }

    private static void EnsureCategoriesLoaded()
    {
        if (_categorySites != null)
        {
            return;
        }

        lock (LoadLock)
        {
            if (_categorySites != null)
            {
                return;
            }

            var jsonPath = Path.Combine(AppContext.BaseDirectory, "Modules", "SiteBlocker", "Data",
                "blocked_sites.json");

            if (!File.Exists(jsonPath))
            {
                _categorySites = new Dictionary<string, string[]>();
                return;
            }

            try
            {
                var json = File.ReadAllText(jsonPath);
                _categorySites = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
                                 ?? new Dictionary<string, string[]>();
            }
            catch
            {
                _categorySites = new Dictionary<string, string[]>();
            }
        }
    }

    private static List<KeyValuePair<string, string>> GetCategoryChoices()
    {
        EnsureCategoriesLoaded();

        var descriptions = new Dictionary<string, string>
        {
            ["Social"] = "Social Media (Facebook, Twitter, Instagram, TikTok, Reddit...)",
            ["Video"] = "Video Platforms (YouTube, Twitch, Vimeo...)",
            ["Streaming"] = "Streaming Services (Netflix, Disney+, HBO...)",
            ["Gaming"] = "Gaming Sites (Steam Store, Epic, IGN...)",
            ["News"] = "News & Media (CNN, BBC, Medium...)",
            ["Shopping"] = "Shopping (Amazon, eBay, AliExpress...)",
            ["Music"] = "Music Streaming (Spotify, SoundCloud...)",
            ["Work"] = "Work & Productivity (Slack, Teams, Gmail...)",
            ["Adult"] = "Adult Content",
            ["Gambling"] = "Gambling & Betting",
            ["Dating"] = "Dating Apps (Tinder, Bumble...)",
            ["Forums"] = "Forums & Communities (Reddit, Quora...)"
        };

        var choices = new List<KeyValuePair<string, string>>();
        if (_categorySites == null)
        {
            return choices;
        }

        foreach (var category in _categorySites.Keys)
        {
            var description = descriptions.TryGetValue(category, out var desc) ? desc : category;
            choices.Add(new KeyValuePair<string, string>(category, description));
        }

        return choices;
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

            await pipeClient.WriteAsync(buffer);
            await pipeClient.FlushAsync();

            var commandName = message.GetType().GetProperty("command")?.GetValue(message) ?? "unknown";
            logger.LogInfo("Command '{Command}' sent successfully via Named Pipe.", commandName);
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex,
                "Could not connect to the Axorith Shim process via Named Pipe. Is the browser extension installed and running?");
            notifier.ShowToast("Site Blocker: Browser extension not connected. Install extension and restart browser.",
                NotificationType.Error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send command to Shim via Named Pipe.");
            notifier.ShowToast("Site Blocker: Failed to communicate with browser extension.", NotificationType.Error);
        }
    }

    private async Task OpenFirefoxExtensionPageAsync()
    {
        const string url = "https://addons.mozilla.org/firefox/addon/axorith-site-blocker/";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            notifier.ShowToast("Firefox extension page opened in your browser", NotificationType.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open Firefox extension page in browser");
            notifier.ShowToast("Failed to open browser. Please manually visit: " + url, NotificationType.Error);
        }

        await Task.Delay(100);
    }

    private async Task OpenChromeExtensionPageAsync()
    {
        const string url = "https://chromewebstore.google.com/detail/axorith-site-blocker/";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            notifier.ShowToast("Chrome extension page opened in your browser", NotificationType.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open Chrome extension page in browser");
            notifier.ShowToast("Failed to open browser. Please manually visit: " + url, NotificationType.Error);
        }

        await Task.Delay(100);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_activeSiteList.Count > 0)
        {
            logger.LogWarning(
                "Disposing module while sites are still blocked. Attempting to send final unblock command.");
            _activeSiteList.Clear();

            _ = Task.Run(async () =>
            {
                try
                {
                    var message = new { command = "unblock" };
                    await using var pipeClient =
                        new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await pipeClient.ConnectAsync(cts.Token);
                    var json = JsonSerializer.Serialize(message);
                    var buffer = Encoding.UTF8.GetBytes(json);
                    await pipeClient.WriteAsync(buffer, cts.Token);
                    await pipeClient.FlushAsync(cts.Token);
                }
                catch
                {
                }
            });
        }

        _mode.Dispose();
        _categories.Dispose();
        _customSites.Dispose();
    }
}