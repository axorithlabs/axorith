using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;

namespace Axorith.Module.Test;

/// <summary>
///     A test module to demonstrate the capabilities of the Axorith SDK
///     and to verify that the Core loads and interacts with modules correctly.
/// </summary>
public class Module(
    IModuleLogger logger,
    IHttpClientFactory httpClientFactory,
    ISecureStorageService secureStorage,
    IEventAggregator eventAggregator,
    ModuleDefinition definition) : IModule
{
    private readonly IHttpClient _httpClient = httpClientFactory.CreateClient($"{definition.Name}.Api");
    
    private IDisposable? _subscription;

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
                5),
            
            new SecretSetting(
                "UserSecret",
                "User Secret",
                "A secret value for testing secure storage."),

            new ChoiceSetting(
                "ProcessingMode",
                "Processing Mode",
                [
                    new KeyValuePair<string, string>("fast", "Fast Mode"),
                    new KeyValuePair<string, string>("accurate", "Accurate Mode"),
                    new KeyValuePair<string, string>("balanced", "Balanced (Default)")
                ],
                "balanced",
                "Choose the processing algorithm."),

            new FilePickerSetting(
                "InputFile",
                "Input File",
                "Select a configuration file.",
                "",
                "JSON files (*.json)|*.json|All files (*.*)|*.*"),

            new DirectoryPickerSetting(
                "OutputDirectory",
                "Output Directory",
                "Select a directory for output files.",
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
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
            logger.LogWarning("Could not parse 'WorkDurationSeconds'. Using default value: {Duration}s", duration);
        }

        logger.LogInfo("User setting 'GreetingMessage': {Message}", message);

        if (enableExtraLogging)
        {
            logger.LogDebug("Simulating work for {Duration} seconds with extra logging.", duration);
            for (var i = (int)duration; i > 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogDebug("{SecondsLeft} seconds left.", i);
                await Task.Delay(1000, cancellationToken);
            }
        }
        else
        {
            logger.LogDebug("Simulating work for {Duration} seconds without extra logging.", duration);
            await Task.Delay((int)duration * 1000, cancellationToken);
        }

        try
        {
            var responseJson =
                await _httpClient.GetStringAsync("https://jsonplaceholder.typicode.com/todos/1", cancellationToken);

            using var jsonDoc = JsonDocument.Parse(responseJson);
            var root = jsonDoc.RootElement;

            var userId = root.TryGetProperty("userId", out var el) ? el.GetInt32() : -1;
            var title = root.TryGetProperty("title", out el) ? el.GetString() : "N/A";
            var completed = root.TryGetProperty("completed", out el) && el.GetBoolean();

            logger.LogInfo("Parsed To-Do Item:");
            logger.LogInfo("  User ID: {UserId}", userId);
            logger.LogInfo("  Title: '{Title}'", title!);
            logger.LogInfo("  Completed: {IsCompleted}", completed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }

        var token = secureStorage.RetrieveSecret("AccessToken");

        if (token is null)
        {
            var newToken = "qwerpoqwprfjofpjweof";
            secureStorage.StoreSecret("AccessToken", newToken);
            token = newToken;
        }

        logger.LogInfo("token {token}", token);

        // --- Event Aggregator Test ---
        logger.LogInfo("Subscribing to TestEvent...");
        _subscription = eventAggregator.Subscribe<TestEvent>(HandleTestEvent);

        logger.LogInfo("Publishing TestEvent in 2 seconds...");
        await Task.Delay(2000, cancellationToken);
        eventAggregator.Publish(new TestEvent { Message = "Hello from Event Aggregator!" });
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync()
    {
        // Perform any cleanup here. For this module, there's nothing to clean up.
        return Task.CompletedTask;
    }

    private void HandleTestEvent(TestEvent evt)
    {
        logger.LogInfo("Received TestEvent! Message: '{Message}'", evt.Message);
    }

    /// <summary>
    ///     Releases any resources used by the module. For TestModule, there's nothing to release.
    /// </summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}