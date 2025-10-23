using Axorith.Sdk;
using Axorith.Sdk.Settings;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Axorith.Module.SiteBlocker;

/// <summary>
/// A module that blocks websites by sending commands to the Axorith.Shim process
/// via a Named Pipe, which then relays them to a browser extension.
/// </summary>
public class Module : IModule
{
    private const string PipeName = "axorith-nm-pipe";
    private readonly ModuleDefinition _definition;
    private readonly IModuleLogger _logger;

    private List<string> _activeBlockedSites = [];

    public Module(ModuleDefinition definition, IServiceProvider serviceProvider)
    {
        _definition = definition;
        _logger = (IModuleLogger)serviceProvider.GetService(typeof(IModuleLogger))!;
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingBase> GetSettings()
    {
        return new List<SettingBase>
        {
            new TextSetting(
                key: "BlockedSites",
                label: "Sites to Block",
                description: "A comma-separated list of domains to block (e.g., youtube.com, twitter.com, reddit.com).",
                isMultiLine: true
            )
        };
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings) => null;

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public Task OnSessionStartAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        _logger.LogInfo("Module '{Name}': Sending 'block' command via Named Pipe...", _definition.Name);

        var sitesString = userSettings.GetValueOrDefault("BlockedSites", string.Empty);
        _activeBlockedSites = sitesString.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

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
    public Task OnSessionEndAsync()
    {
        _logger.LogInfo("Module '{Name}': Sending 'unblock' command via Named Pipe...", _definition.Name);

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
    /// Asynchronously sends a command object to the Shim via a named pipe.
    /// </summary>
    private async Task WriteToPipeAsync(object message)
    {
        try
        {
            // The client connects to the pipe server hosted by the Shim.
            await using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            
            // We give it a short timeout to connect. If the Shim isn't running,
            // we don't want to hang the session indefinitely.
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
            _logger.LogError(ex, "Could not connect to the Axorith Shim process via Named Pipe. Is the browser extension installed and running?");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to Shim via Named Pipe.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // If the module is disposed while a session is active, send a final unblock command.
        if (_activeBlockedSites.Count > 0)
        {
            _logger.LogWarning("Disposing module while sites are still blocked. Attempting to send final unblock command.");
            var message = new { command = "unblock" };
            // Fire-and-forget the async method in a synchronous context.
            _ = WriteToPipeAsync(message);
        }
    }
}
