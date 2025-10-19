using Axorith.Sdk;
using Axorith.Sdk.Settings;

namespace Axorith.Module.Test;

/// <summary>
/// A test module to demonstrate the capabilities of the Axorith SDK
/// and to verify that the Core loads and interacts with modules correctly.
/// </summary>
public class TestModule : IModule
{
    /// <inheritdoc />
    public Guid Id => Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
    
    /// <inheritdoc />
    public string Name => "Test Module";
    
    /// <inheritdoc />
    public string Description => "A simple module for testing and demonstration purposes. It logs messages and simulates work.";
    
    /// <inheritdoc />
    public string Category => "System";
    
    /// <inheritdoc />
    public IReadOnlySet<Platform> SupportedPlatforms => new HashSet<Platform> { Platform.Windows, Platform.Linux, Platform.MacOs };

    /// <inheritdoc />
    public IReadOnlyList<SettingBase> GetSettings()
    {
        return new List<SettingBase>
        {
            new TextSetting(
                key: "GreetingMessage", 
                label: "Greeting Message", 
                description: "This message will be logged on session start.", 
                defaultValue: "Hello from TestModule!"),
            
            new CheckboxSetting(
                key: "EnableExtraLogging", 
                label: "Enable Extra Logging", 
                description: "If checked, the module will log a countdown.", 
                defaultValue: true),
            
            new NumberSetting(
                key: "WorkDurationSeconds", 
                label: "Work Duration (sec)", 
                description: "How long the module should simulate work.", 
                defaultValue: 5)
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
    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        // Try to get the value for 'WorkDurationSeconds'. If it doesn't exist, settings are considered valid.
        if (!userSettings.TryGetValue("WorkDurationSeconds", out var durationStr))
            return Task.FromResult(ValidationResult.Success);
        
        // If the value exists, ensure it's a non-negative number.
        if (!decimal.TryParse(durationStr, out var duration) || duration < 0)
        {
            return Task.FromResult(ValidationResult.Fail("'Work Duration' must be a non-negative number."));
        }
        
        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(IModuleContext context, IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        context.LogInfo("Test Module is starting...");

        // Retrieve settings safely, using default values from the module definition if a key is not found.
        var message = userSettings.GetValueOrDefault("GreetingMessage", "Default Greeting");
        
        // For booleans, parse the string value, defaulting to 'true' if not found.
        var enableExtraLogging = bool.Parse(userSettings.GetValueOrDefault("EnableExtraLogging", "true"));
        
        // For numbers, parse with a fallback and log a warning on failure.
        if (!decimal.TryParse(userSettings.GetValueOrDefault("WorkDurationSeconds", "5"), out var duration))
        {
            duration = 5;
            context.LogWarning("Could not parse 'WorkDurationSeconds'. Using default value: {Duration}s", duration);
        }

        context.LogInfo("User setting 'GreetingMessage': {Message}", message);

        if (enableExtraLogging)
        {
            context.LogDebug("Simulating work for {Duration} seconds with extra logging.", duration);
            for (var i = (int)duration; i > 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.LogDebug("... {SecondsLeft} seconds left.", i);
                await Task.Delay(1000, cancellationToken);
            }
        }
        else
        {
            context.LogDebug("Simulating work for {Duration} seconds without extra logging.", duration);
            await Task.Delay((int)duration * 1000, cancellationToken);
        }

        context.LogInfo("Test Module has finished its work.");
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync(IModuleContext context)
    {
        context.LogInfo("Test Module has been requested to shut down.");
        // Perform any cleanup here. For this module, there's nothing to clean up.
        return Task.CompletedTask;
    }
}