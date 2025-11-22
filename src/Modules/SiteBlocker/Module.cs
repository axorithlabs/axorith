using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.SiteBlocker;

/// <summary>
///     A module that blocks websites by sending commands to the Axorith.Shim process
///     via a Named Pipe, which then relays them to a browser extension.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;

    private readonly Setting<string> _mode;
    private readonly Setting<string> _siteList;

    // In Debug mode, we register a separate Native Messaging Host ("axorith.dev").
    // This allows developers to run the project from the IDE without overwriting
    // or conflicting with the installed production version ("axorith").
    // The browser extension is updated to try connecting to "axorith.dev" first.
    #if DEBUG
    private const string HostName = "axorith.dev";
    private const string ManifestType = "dev";
    #else
    private const string HostName = "axorith";
    private const string ManifestType = "prod";
    #endif

    private readonly string _pipeName = "axorith-nm-pipe";

    private List<string> _activeSiteList = [];

    public Module(IModuleLogger logger)
    {
        _logger = logger;

        _mode = Setting.AsChoice(
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

        _siteList = Setting.AsTextArea(
            key: "BlockedSites",
            label: "Site List",
            description:
            "A comma-separated list of domains (e.g., youtube.com, twitter.com). Behavior depends on Mode.",
            defaultValue: "youtube.com, twitter.com, reddit.com"
        );

        try
        {
            var manifestPath = EnsureManifest();

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                _logger.LogWarning("Manifest missing at {Path}", manifestPath ?? "<null>");
                return;
            }

            PublicApi.EnsureFirefoxHostRegistered(HostName, manifestPath);

            _logger.LogInfo("Firefox native host registered: '{HostName}' -> {Path}", HostName, manifestPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Firefox native host");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return [_mode, _siteList];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return [];
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
        _logger.LogInfo("Sending 'block' command via Named Pipe (Mode: {Mode})...", mode);

        _activeSiteList =
        [
            .. _siteList.GetCurrentValue().Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
        ];

        if (_activeSiteList.Count == 0)
        {
            _logger.LogWarning("No sites specified. Module will do nothing.");
            return Task.CompletedTask;
        }

        _logger.LogDebug("Sites: {Sites}", string.Join(", ", _activeSiteList));

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
        _logger.LogInfo("Sending 'unblock' command via Named Pipe...");

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
            _logger.LogInfo("Command '{Command}' sent successfully via Named Pipe.", commandName);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex,
                "Could not connect to the Axorith Shim process via Named Pipe. Is the browser extension installed and running?");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to Shim via Named Pipe.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_activeSiteList.Count <= 0)
        {
            return;
        }

        _logger.LogWarning(
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
                        _logger.LogWarning("Unblock command timed out during disposal");
                    }
                }
                catch (Exception)
                {
                    _logger.LogWarning("Failed to send unblock command during disposal");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send unblock command during disposal");
        }
        finally
        {
            _activeSiteList.Clear();
        }
    }

    private string? EnsureManifest()
    {
        try
        {
            var shimDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../Axorith.Shim"));

            if (!Directory.Exists(shimDir))
            {
                _logger.LogWarning("Axorith.Shim directory not found at {ShimDir}", shimDir);
                return null;
            }

            var templatePath = Path.Combine(shimDir, "axorith.json");
            if (!File.Exists(templatePath))
            {
                _logger.LogWarning("Native messaging manifest template not found at {TemplatePath}", templatePath);
                return null;
            }

            var shimPath = Path.Combine(shimDir, "Axorith.Shim.exe");
            if (string.IsNullOrWhiteSpace(shimPath) || !File.Exists(shimPath))
            {
                _logger.LogWarning("Axorith.Shim executable was not found in {ShimDir}", shimDir);
                return null;
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var manifestDir = Path.Combine(appData, "Axorith", "native-messaging", ManifestType);
            Directory.CreateDirectory(manifestDir);

            var manifestPath = Path.Combine(manifestDir, $"axorith.{ManifestType}.firefox.json");

            var json = File.ReadAllText(templatePath, Encoding.UTF8);
            JsonNode? rootNode;
            try
            {
                rootNode = JsonNode.Parse(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse native messaging manifest template at {TemplatePath}",
                    templatePath);
                return null;
            }

            if (rootNode is not JsonObject obj)
            {
                _logger.LogWarning("Native messaging manifest template root is not a JSON object: {TemplatePath}",
                    templatePath);
                return null;
            }

            obj["path"] = shimPath;
            obj["name"] = HostName; // "axorith" or "axorith.dev"

            var outputJson = obj.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(manifestPath, outputJson, Encoding.UTF8);
            _logger.LogInfo("Native messaging manifest written to {ManifestPath}", manifestPath);

            return manifestPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while preparing native messaging manifest");
            return null;
        }
    }
}