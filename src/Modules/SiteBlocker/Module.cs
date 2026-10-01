using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SiteBlocker;

public class Module(IModuleLogger logger, INotifier notifier, IProcessBlocker browserBlocker) : IModule, ISessionBreakParticipant,
    ICommittedSessionValidator
{
    private static Dictionary<string, string[]>? _categorySites;
    private static readonly Lock _loadLock = new();

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

    private sealed record BrowserEndpoint(string Name, string PipeName, string[] Processes, string ResponseBrowser);
    private sealed record ExtensionResult(BrowserEndpoint Endpoint, string Status, string? Version,
        bool Blocking, string? Message);

    private const int ExtensionProtocolVersion = 1;
    private static readonly BrowserEndpoint[] BrowserEndpoints =
    [
        new("Chrome", "axorith-nm-pipe-chrome", ["chrome"], "chromium"),
        new("Edge", "axorith-nm-pipe-edge", ["msedge"], "chromium"),
        new("Chromium", "axorith-nm-pipe-chromium", ["chromium"], "chromium"),
        new("Firefox", "axorith-nm-pipe-firefox", ["firefox"], "firefox")
    ];

    private List<string> _activeSiteList = [];
    private bool _isAllowList;
    private bool _pausedForBreak;
    private readonly HashSet<string> _connectedBrowsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _browserStatuses = BrowserEndpoints.ToDictionary(
        endpoint => endpoint.Name, _ => "Missing", StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _fallbackBrowserProcesses = new(StringComparer.OrdinalIgnoreCase);
    private bool _browserFallbackFailed;
    private bool _disposed;

    public bool IsProtectionDegraded => _browserFallbackFailed ||
        ((_activeSiteList.Count > 0 || _isAllowList) && !_pausedForBreak &&
         _browserStatuses.Values.Any(status => status != "Connected"));

    public string? ProtectionStatusMessage => _activeSiteList.Count == 0 && !_isAllowList
        ? "Site Blocker has no configured sites"
        : _pausedForBreak
            ? "Site Blocker paused for break"
            : "Site Blocker · " + string.Join(", ", BrowserEndpoints.Select(endpoint =>
        {
            var status = _browserStatuses[endpoint.Name];
            var isFallbackBlocked = endpoint.Processes.Any(_fallbackBrowserProcesses.Contains);
            return $"{endpoint.Name} {status}{(isFallbackBlocked ? " (browser blocked)" : string.Empty)}";
        })) + (_browserFallbackFailed ? " · browser fallback failed" : string.Empty);

    public IReadOnlyList<ISetting> GetSettings()
    {
        return [_mode, _categories, _customSites];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        var installFirefoxAction = Action.Create("InstallExtension.Firefox", "Install Firefox Extension");
        installFirefoxAction.OnInvokeAsync(InstallFirefoxExtensionAsync);

        return [installFirefoxAction];
    }

    public async Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        var categories = _categories.GetCurrentValue();
        var custom = _customSites.GetCurrentValue();
        var allowList = _mode.GetCurrentValue() == "AllowList";
        var noSitesConfigured = categories.Count == 0 && string.IsNullOrWhiteSpace(custom) && !allowList;

        var extensionResults = await SendToAllExtensionsAsync(new { command = "health" }, cancellationToken)
            .ConfigureAwait(false);
        UpdateBrowserStatuses(extensionResults);

        var warnings = new List<string>();
        if (noSitesConfigured)
        {
            warnings.Add("No categories or sites selected. The module will not block anything.");
        }

        if (!extensionResults.Any(result => result.Status == "Connected"))
        {
            warnings.Add(
                "No Site Blocker browser extension is connected. Open Chrome or Firefox with the extension installed.");
        }

        return warnings.Count == 0 ? ValidationResult.Success : ValidationResult.Warn(string.Join(" ", warnings));
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        _activeSiteList = GetAllSites();
        _isAllowList = _mode.GetCurrentValue() == "AllowList";
        _pausedForBreak = false;
        if (_activeSiteList.Count == 0 && !_isAllowList)
        {
            logger.LogWarning("No sites specified. Module will do nothing.");
            return;
        }

        logger.LogInfo("Sending site-block command to supported browsers ({Count} sites).", _activeSiteList.Count);
        var results = await SendToAllExtensionsAsync(new
        {
            command = "block",
            mode = _isAllowList ? "AllowList" : "BlockList",
            sites = _activeSiteList
        }, cancellationToken).ConfigureAwait(false);
        UpdateBrowserStatuses(results);
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_activeSiteList.Count > 0 || _isAllowList)
        {
            await SendToAllExtensionsAsync(new { command = "unblock" }, cancellationToken).ConfigureAwait(false);
        }

        _activeSiteList.Clear();
        _isAllowList = false;
        _pausedForBreak = false;
        ApplyBrowserFallback([]);
    }

    public async Task PauseForBreakAsync(CancellationToken cancellationToken)
    {
        if (_activeSiteList.Count == 0 && !_isAllowList)
        {
            return;
        }

        var results = await SendToAllExtensionsAsync(new { command = "unblock" }, cancellationToken)
            .ConfigureAwait(false);
        UpdateBrowserStatuses(results);
        _pausedForBreak = true;
        ApplyBrowserFallback([]);
    }

    public async Task ResumeAfterBreakAsync(CancellationToken cancellationToken)
    {
        if (_activeSiteList.Count == 0 && !_isAllowList)
        {
            return;
        }

        var results = await SendToAllExtensionsAsync(new
        {
            command = "block",
            mode = _isAllowList ? "AllowList" : "BlockList",
            sites = _activeSiteList
        }, cancellationToken).ConfigureAwait(false);
        _pausedForBreak = false;
        UpdateBrowserStatuses(results);
        ApplyBrowserFallback(results.Where(result => result.Status != "Connected"));
    }

    public async Task<bool> IsProtectionHealthyAsync(CancellationToken cancellationToken)
    {
        if (_activeSiteList.Count == 0 && !_isAllowList)
        {
            return true;
        }

        if (_pausedForBreak)
        {
            var unblockResults = await SendToAllExtensionsAsync(new { command = "unblock" }, cancellationToken)
                .ConfigureAwait(false);
            UpdateBrowserStatuses(unblockResults);
            ApplyBrowserFallback([]);
            return true;
        }

        var results = (await SendToAllExtensionsAsync(new
        {
            command = "health",
            mode = _isAllowList ? "AllowList" : "BlockList",
            sites = _activeSiteList
        }, cancellationToken).ConfigureAwait(false)).ToList();

        for (var i = 0; i < results.Count; i++)
        {
            if (results[i].Status == "Connected" && !results[i].Blocking)
            {
                results[i] = await SendToExtensionAsync(results[i].Endpoint, new
                {
                    command = "block",
                    mode = _mode.GetCurrentValue(),
                    sites = _activeSiteList
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        UpdateBrowserStatuses(results);
        ApplyBrowserFallback(results.Where(result => result.Status != "Connected"));
        return results.Any(result => result.Status == "Connected") && !_browserFallbackFailed;
    }

    public async Task<bool> CanStartCommittedSessionAsync(CancellationToken cancellationToken)
    {
        if (GetAllSites().Count == 0 && _mode.GetCurrentValue() != "AllowList")
        {
            return true;
        }

        var results = await SendToAllExtensionsAsync(new { command = "health" }, cancellationToken)
            .ConfigureAwait(false);
        UpdateBrowserStatuses(results);
        return results.Any(result => result.Status == "Connected");
    }

    private Task<ExtensionResult[]> SendToAllExtensionsAsync(object message, CancellationToken cancellationToken) =>
        Task.WhenAll(BrowserEndpoints.Select(endpoint => SendToExtensionAsync(endpoint, message, cancellationToken)));

    private async Task<ExtensionResult> SendToExtensionAsync(BrowserEndpoint endpoint, object message,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var request = JsonSerializer.SerializeToNode(message)?.AsObject()
                      ?? throw new InvalidOperationException("Could not serialize Site Blocker request.");
        request["requestId"] = requestId;
        request["protocolVersion"] = ExtensionProtocolVersion;
        var connectedToPipe = false;

        try
        {
            await using var pipeClient = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipeClient.ConnectAsync(250, cancellationToken).ConfigureAwait(false);
            connectedToPipe = true;

            using var writer = new StreamWriter(pipeClient, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipeClient, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await writer.WriteLineAsync(request.ToJsonString().AsMemory(), linked.Token).ConfigureAwait(false);
            var responseText = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new ExtensionResult(endpoint, "Error", null, false, "The native host returned no response.");
            }

            using var response = JsonDocument.Parse(responseText);
            var root = response.RootElement;
            if (!root.TryGetProperty("requestId", out var responseId) || responseId.GetString() != requestId)
            {
                return new ExtensionResult(endpoint, "Error", null, false, "The extension response did not match the request.");
            }

            var protocolVersion = root.TryGetProperty("protocolVersion", out var versionElement) &&
                                  versionElement.TryGetInt32(out var parsedProtocolVersion)
                ? parsedProtocolVersion
                : 0;
            if (protocolVersion != ExtensionProtocolVersion)
            {
                return new ExtensionResult(endpoint, "Outdated", null, false, "The extension protocol is outdated.");
            }

            if (root.TryGetProperty("status", out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.String &&
                statusElement.GetString() is { } errorStatus && errorStatus != "Connected")
            {
                return new ExtensionResult(endpoint, errorStatus, null, false,
                    root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null);
            }

            if (!root.TryGetProperty("browser", out var browserElement) ||
                browserElement.GetString() != endpoint.ResponseBrowser)
            {
                return new ExtensionResult(endpoint, "Error", null, false, "Unexpected browser identity.");
            }

            var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
            var active = root.TryGetProperty("blocking", out var blockingElement) && blockingElement.GetBoolean();
            var extensionVersion = root.TryGetProperty("version", out var extensionVersionElement)
                ? extensionVersionElement.GetString()
                : null;
            return new ExtensionResult(endpoint, ok ? "Connected" : "Error", extensionVersion, active,
                ok ? null : "The extension reported a blocking error.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            return new ExtensionResult(endpoint, connectedToPipe ? "Outdated" : GetUnavailableStatus(endpoint),
                null, false, ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            return new ExtensionResult(endpoint, connectedToPipe ? "Outdated" : GetUnavailableStatus(endpoint),
                null, false, ex.Message);
        }
        catch (Exception ex)
        {
            return new ExtensionResult(endpoint, connectedToPipe ? "Error" : GetUnavailableStatus(endpoint),
                null, false, ex.Message);
        }
    }

    private string GetUnavailableStatus(BrowserEndpoint endpoint)
    {
        if (_connectedBrowsers.Contains(endpoint.Name) || IsBrowserRunning(endpoint.Processes))
        {
            return "Disconnected";
        }

        return "Missing";
    }

    private static bool IsBrowserRunning(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length == 0)
            {
                continue;
            }

            foreach (var process in processes)
            {
                process.Dispose();
            }

            return true;
        }

        return false;
    }

    private void UpdateBrowserStatuses(IEnumerable<ExtensionResult> results)
    {
        foreach (var result in results)
        {
            _browserStatuses[result.Endpoint.Name] = result.Status;
            if (result.Status == "Connected")
            {
                _connectedBrowsers.Add(result.Endpoint.Name);
            }
        }
    }

    private void ApplyBrowserFallback(IEnumerable<ExtensionResult> unavailable)
    {
        var processes = unavailable.SelectMany(result => result.Endpoint.Processes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (processes.SetEquals(_fallbackBrowserProcesses) && !_browserFallbackFailed)
        {
            return;
        }

        try
        {
            if (processes.Count == 0)
            {
                browserBlocker.UnblockAll();
            }
            else
            {
                browserBlocker.Block(processes);
            }

            _fallbackBrowserProcesses = processes;
            _browserFallbackFailed = false;
        }
        catch (Exception ex)
        {
            _browserFallbackFailed = true;
            logger.LogError(ex, "Could not apply browser process fallback for Site Blocker.");
        }
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

        lock (_loadLock)
        {
            if (_categorySites != null)
            {
                return;
            }

            var moduleDirectory = Path.GetDirectoryName(typeof(Module).Assembly.Location);
            if (string.IsNullOrEmpty(moduleDirectory))
            {
                _categorySites = new Dictionary<string, string[]>();
                return;
            }

            var jsonPath = Path.Combine(moduleDirectory, "Data", "blocked_sites.json");

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

    private Task InstallFirefoxExtensionAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            notifier.ShowToast("Automatic browser extension installation is supported on Windows only.",
                NotificationType.Error, "Site Blocker");
            return Task.CompletedTask;
        }

        try
        {
            RegisterFirefoxExtensionPolicy();
            var firefoxIsRunning = IsBrowserRunning(["firefox"]);
            if (!firefoxIsRunning)
                Process.Start(new ProcessStartInfo("firefox.exe") { UseShellExecute = true });

            notifier.ShowToast(firefoxIsRunning
                    ? "Firefox is running. Site Blocker will install automatically the next time Firefox starts."
                    : "Firefox opened. Site Blocker will install automatically.",
                firefoxIsRunning ? NotificationType.Warning : NotificationType.Success, "Site Blocker");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure automatic Firefox extension installation");
            notifier.ShowToast("Could not configure automatic Firefox extension installation.",
                NotificationType.Error, "Site Blocker");
        }

        return Task.CompletedTask;
    }

    private static void RegisterFirefoxExtensionPolicy()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Firefox extension policies are available on Windows only.");

        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Policies\Mozilla\Firefox\ExtensionSettings", writable: true)
            ?? throw new InvalidOperationException("Could not create the Firefox extension policy key.");

        var policies = new JsonObject();
        var existingValues = key.GetValue(string.Empty) switch
        {
            string[] values => values,
            string value => [value],
            _ => []
        };

        foreach (var value in existingValues)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (JsonNode.Parse(value) is not JsonObject existing)
                continue;

            foreach (var policy in existing)
                policies[policy.Key] = policy.Value?.DeepClone();
        }

        policies[Axorith.Shared.Utils.SiteBlockerExtensionIds.Firefox] = new JsonObject
        {
            ["installation_mode"] = "normal_installed",
            ["install_url"] = "https://addons.mozilla.org/firefox/downloads/latest/axorith-site-blocker/latest.xpi"
        };

        key.SetValue(string.Empty, new[] { policies.ToJsonString() }, RegistryValueKind.MultiString);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_activeSiteList.Count > 0 || _isAllowList)
        {
            logger.LogWarning(
                "Disposing module while sites are still blocked. Attempting to send final unblock command.");
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendToAllExtensionsAsync(new { command = "unblock" }, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            });
            _activeSiteList.Clear();
        }

        ApplyBrowserFallback([]);

        _mode.Dispose();
        _categories.Dispose();
        _customSites.Dispose();
    }
}
