using Axorith.Sdk;
using Axorith.Sdk.Settings;

namespace Axorith.Module.Test;

/// <summary>
///     A test module to demonstrate the capabilities of the Axorith SDK
///     and to verify that the Core loads and interacts with modules correctly.
/// </summary>
public class Module : IModule
{
    private ModuleDefinition _definition;
    private IServiceProvider _serviceProvider;
    private IModuleLogger _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TestModule" /> class.
    ///     Dependencies are injected by the Core.
    /// </summary>
    public Module(ModuleDefinition definition, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _definition = definition;
        // Resolve the _logger from the provided service provider.

        _logger = (IModuleLogger)serviceProvider.GetService(typeof(IModuleLogger))!;
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingBase> GetSettings()
    {
        return new List<SettingBase>
        {
            new TextSetting(
                "GreetingMessage",
                "Greeting Message",
                "This message will be logged on session start.",
                "Hello from TestModule!"),

            new CheckboxSetting(
                "EnableExtraLogging",
                "Enable Extra Logging",
                "If checked, the module will log a countdown.",
                true),

            new NumberSetting(
                "WorkDurationSeconds",
                "Work Duration (sec)",
                "How long the module should simulate work.",
                5)
        };
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings)
    {
        // This module uses auto-generated UI, so this method returns null.
        return null;
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        // Try to get the value for 'WorkDurationSeconds'. If it doesn't exist, settings are considered valid.
        if (!userSettings.TryGetValue("WorkDurationSeconds", out var durationStr))
            return Task.FromResult(ValidationResult.Success);

        // If the value exists, ensure it's a non-negative number.
        if (!decimal.TryParse(durationStr, out var duration) || duration < 0)
            return Task.FromResult(ValidationResult.Fail("'Work Duration' must be a non-negative number."));

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        // Retrieve settings safely, using default values from the module definition if a key is not found.
        var message = userSettings.GetValueOrDefault("GreetingMessage", "Default Greeting");

        // For booleans, parse the string value, defaulting to 'true' if not found.
        var enableExtraLogging = bool.Parse(userSettings.GetValueOrDefault("EnableExtraLogging", "true"));

        // For numbers, parse with a fallback and log a warning on failure.
        if (!decimal.TryParse(userSettings.GetValueOrDefault("WorkDurationSeconds", "5"), out var duration))
        {
            duration = 5;
            _logger.LogWarning("Could not parse 'WorkDurationSeconds'. Using default value: {Duration}s", duration);
        }

        _logger.LogInfo("User setting 'GreetingMessage': {Message}", message);

        if (enableExtraLogging)
        {
            _logger.LogDebug("Simulating work for {Duration} seconds with extra logging.", duration);
            for (var i = (int)duration; i > 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogDebug("{SecondsLeft} seconds left.", i);
                await Task.Delay(1000, cancellationToken);
            }
        }
        else
        {
            _logger.LogDebug("Simulating work for {Duration} seconds without extra logging.", duration);
            await Task.Delay((int)duration * 1000, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync()
    {
        // Perform any cleanup here. For this module, there's nothing to clean up.
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Releases any resources used by the module. For TestModule, there's nothing to release.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}