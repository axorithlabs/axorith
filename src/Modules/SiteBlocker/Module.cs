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

    private readonly Setting<string> _blockedSites;
    private readonly Setting<string> _status;

    #if DEBUG
    private const string HostName = "axorith.dev";
    #else
    private const string HostName = "axorith";
    #endif

    private readonly string _pipeName = "axorith-nm-pipe";

    private List<string> _activeBlockedSites = [];

    public Module(IModuleLogger logger)
    {
        _logger = logger;

        _blockedSites = Setting.AsTextArea(
            key: "BlockedSites",
            label: "Sites to Block",
            description: "A comma-separated list of domains to block (e.g., youtube.com, twitter.com, reddit.com).",
            defaultValue: "youtube.com, twitter.com, reddit.com"
        );

        _status = Setting.AsText(key: "ShimStatus",
            label: "Shim / Browser Status",
            defaultValue: string.Empty,
            isReadOnly: true,
            description: "Connection status between SiteBlocker, Axorith Shim, and the browser extension.");

        try
        {
            var manifestPath = EnsureManifest();

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                _logger.LogWarning("Manifest missing at {Path}", manifestPath ?? "<null>");
                return;
            }

            PublicApi.EnsureFirefoxHostRegistered(HostName, manifestPath);

            _logger.LogInfo("Firefox native host registered: {Path}", manifestPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Firefox native host");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return [_blockedSites, _status];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return [];
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("Sending 'block' command via Named Pipe...");

        _activeBlockedSites =
        [
            .. _blockedSites.GetCurrentValue().Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
        ];

        if (_activeBlockedSites.Count == 0)
        {
            _logger.LogWarning("No sites specified for blocking. Module will do nothing.");
            return Task.CompletedTask;
        }

        _logger.LogDebug("Sites to block: {Sites}", string.Join(", ", _activeBlockedSites));

        var message = new { command = "block", sites = _activeBlockedSites };
        return WriteToPipeAsync(message);
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("Sending 'unblock' command via Named Pipe...");

        if (_activeBlockedSites.Count == 0)
        {
            return Task.CompletedTask;
        }

        var message = new { command = "unblock" };
        var resultTask = WriteToPipeAsync(message);
        _activeBlockedSites.Clear();
        return resultTask;
    }

    /// <summary>
    ///     Asynchronously sends a command object to the Shim via a named pipe.
    /// </summary>
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
            _status.SetValue($"Shim connection OK. Last command: {commandName}.");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex,
                "Could not connect to the Axorith Shim process via Named Pipe. Is the browser extension installed and running?");
            _status.SetValue(
                "Error: Could not connect to Axorith Shim. Ensure Shim is running and the browser extension is installed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to Shim via Named Pipe.");
            _status.SetValue("Error: Failed to send command to Shim: " + ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_activeBlockedSites.Count <= 0)
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
            _activeBlockedSites.Clear();
        }
    }

    private string? EnsureManifest()
    {
        try
        {
            #if DEBUG
            const string type = "dev";
            #else
            const string type = "prod";
            #endif

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
            var manifestDir = Path.Combine(appData, "Axorith", "native-messaging", type);
            Directory.CreateDirectory(manifestDir);

            var manifestPath = Path.Combine(manifestDir, $"axorith.{type}.firefox.json");

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
            obj["name"] = HostName;

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