using Axorith.Sdk;

namespace Axorith.Module.Test;

/// <summary>
/// A test module to demonstrate the capabilities of the Axorith SDK
/// and to verify that the Core loads and interacts with modules correctly.
/// </summary>
public class TestModule : IModule
{
    public Guid Id => Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
    public string Name => "Test Module";
    public string Description => "A simple module for testing and demonstration purposes. It logs messages and simulates work.";
    public string Category => "System";
    public IReadOnlySet<Platform> SupportedPlatforms => new HashSet<Platform> { Platform.Windows, Platform.Linux, Platform.MacOs };

    public IReadOnlyList<ModuleSetting> GetSettings()
    {
        return new List<ModuleSetting>
        {
            new("GreetingMessage", "Greeting Message", SettingType.Text, "Hello from TestModule!", "This message will be logged on session start."),
            new("EnableExtraLogging", "Enable Extra Logging", SettingType.Checkbox, "true", "If checked, the module will log a countdown."),
            new("WorkDurationSeconds", "Work Duration (sec)", SettingType.Number, "5", "How long the module should simulate work.")
        };
    }

    public Type? CustomSettingsViewType => null;

    public object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings)
    {
        return null;
    }

    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        if (!userSettings.TryGetValue("WorkDurationSeconds", out var durationStr))
            return Task.FromResult(ValidationResult.Success);
        
        if (!int.TryParse(durationStr, out var duration) || duration < 0)
        {
            return Task.FromResult(ValidationResult.Fail("'Work Duration' must be a positive number."));
        }
        return Task.FromResult(ValidationResult.Success);
    }

    public async Task OnSessionStartAsync(IModuleContext context, IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        context.LogInfo("Test Module is starting...");

        var message = userSettings.GetValueOrDefault("GreetingMessage", "Default Greeting");
        var enableExtraLogging = bool.Parse(userSettings.GetValueOrDefault("EnableExtraLogging", "true"));
        
        if (!float.TryParse(userSettings.GetValueOrDefault("WorkDurationSeconds", "5"), out var duration))
        {
            duration = 5;
            context.LogWarning("Could not parse 'WorkDurationSeconds'. Using default value: {Duration}s", duration);
        }

        context.LogInfo("User setting 'GreetingMessage': {Message}", message);

        if (enableExtraLogging)
        {
            for (var i = (int)duration; i > 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.LogDebug("Simulating work... {SecondsLeft} seconds left.", i);
                await Task.Delay(1000, cancellationToken);
            }
        }
        else
        {
            await Task.Delay((int)duration * 1000, cancellationToken);
        }

        context.LogInfo("Test Module has finished its work.");
    }

    public Task OnSessionEndAsync(IModuleContext context)
    {
        context.LogInfo("Test Module has been requested to shut down.");
        
        return Task.CompletedTask;
    }
}