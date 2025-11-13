using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;

namespace Axorith.Module.Test;

/// <summary>
///     A test module to demonstrate the capabilities of the Axorith SDK
///     and to verify that the Core loads and interacts with modules correctly.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly IHttpClient _httpClient;
    private readonly ISecureStorageService _secureStorage;
    private readonly IEventAggregator _eventAggregator;

    private readonly Setting<string> _greetingMessage;
    private readonly Setting<bool> _enableExtraLogging;
    private readonly Setting<decimal> _workDurationSeconds;
    private readonly Setting<string> _userSecret;
    private readonly Setting<string> _processingMode;
    private readonly Setting<string> _inputFile;
    private readonly Setting<string> _outputDirectory;
    private readonly Setting<bool> _showNotes;
    private readonly Setting<string> _notes;

    public Module(IModuleLogger logger,
        IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        IEventAggregator eventAggregator,
        ModuleDefinition definition)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient($"{definition.Name}.Api");
        _secureStorage = secureStorage;
        _eventAggregator = eventAggregator;

        _greetingMessage = Setting.AsText(
            key: "GreetingMessage",
            label: "Greeting Message",
            description: "This message will be logged on session start.",
            defaultValue: "Hello from TestModule!"
        );

        _enableExtraLogging = Setting.AsCheckbox(
            key: "EnableExtraLogging",
            label: "Enable Extra Logging",
            description: "If checked, the module will log a countdown.",
            defaultValue: true
        );

        _workDurationSeconds = Setting.AsNumber(
            key: "WorkDurationSeconds",
            label: "Work Duration (sec)",
            description: "How long the module should simulate work.",
            defaultValue: 5
        );

        _userSecret = Setting.AsSecret(
            key: "UserSecret",
            label: "User Secret",
            description: "A secret value for testing secure storage."
        );

        _processingMode = Setting.AsChoice(
            key: "ProcessingMode",
            label: "Processing Mode",
            description: "Choose the processing algorithm.",
            defaultValue: "balanced",
            initialChoices:
            [
                new KeyValuePair<string, string>("fast", "Fast Mode"),
                new KeyValuePair<string, string>("accurate", "Accurate Mode"),
                new KeyValuePair<string, string>("balanced", "Balanced (Default)")
            ]
        );

        _inputFile = Setting.AsFilePicker(
            key: "InputFile",
            label: "Input File",
            description: "Select a configuration file.",
            defaultValue: "",
            filter: "JSON files (*.json)|*.json|All files (*.*)|*.*"
        );

        _outputDirectory = Setting.AsDirectoryPicker(
            key: "OutputDirectory",
            label: "Output Directory",
            description: "Select a directory for output files.",
            defaultValue: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        );

        _showNotes = Setting.AsCheckbox(
            key: "ShowNotes",
            label: "Show Notes",
            description: "Toggle to show/hide the Notes field.",
            defaultValue: false
        );

        _notes = Setting.AsTextArea(
            key: "Notes",
            label: "Notes",
            defaultValue: string.Empty,
            description: "Optional notes to validate UI reactivity.",
            isVisible: false
        );

        _showNotes.Value.Subscribe(visible => _notes.SetVisibility(visible));

        var userSecret = _secureStorage.RetrieveSecret(_userSecret.Key);

        if (string.IsNullOrEmpty(userSecret)) return;

        _userSecret.SetValue(userSecret);
        _logger.LogDebug("Loaded secret '{Key}' from SecureStorage", _userSecret.Key);
    }

    private IDisposable? _subscription;

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return
        [
            _greetingMessage,
            _enableExtraLogging,
            _workDurationSeconds,
            _userSecret,
            _processingMode,
            _inputFile,
            _outputDirectory,
            _showNotes,
            _notes
        ];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return [];
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel()
    {
        // This module uses auto-generated UI, so this method returns null.
        return null;
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        // If the value exists, ensure it's a non-negative number.
        if (_workDurationSeconds.GetCurrentValue() < 0)
            return Task.FromResult(
                ValidationResult.Fail($"{_workDurationSeconds.Label} must be a non-negative number."));

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("User setting 'GreetingMessage': {Message}", _greetingMessage.GetCurrentValue());

        if (_enableExtraLogging.GetCurrentValue())
        {
            _logger.LogDebug("Simulating work for {Duration} seconds with extra logging.",
                _workDurationSeconds.GetCurrentValue());
            for (var i = (int)_workDurationSeconds.GetCurrentValue(); i > 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogDebug("{SecondsLeft} seconds left.", i);
                await Task.Delay(1000, cancellationToken);
            }
        }
        else
        {
            _logger.LogDebug("Simulating work for {Duration} seconds without extra logging.",
                _workDurationSeconds.GetCurrentValue());
            await Task.Delay((int)_workDurationSeconds.GetCurrentValue() * 1000, cancellationToken);
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

            _logger.LogInfo("Parsed To-Do Item:");
            _logger.LogInfo("  User ID: {UserId}", userId);
            _logger.LogInfo("  Title: '{Title}'", title!);
            _logger.LogInfo("  Completed: {IsCompleted}", completed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            throw;
        }

        var token = _secureStorage.RetrieveSecret("AccessToken");

        if (token is null)
        {
            var newToken = "qwerpoqwprfjofpjweof";
            _secureStorage.StoreSecret("AccessToken", newToken);
            token = newToken;
        }

        _logger.LogInfo("token {token}", token);

        _logger.LogInfo("Subscribing to TestEvent...");
        _subscription = _eventAggregator.Subscribe<TestEvent>(HandleTestEvent);

        _logger.LogInfo("Publishing TestEvent in 2 seconds...");
        await Task.Delay(2000, cancellationToken);
        _eventAggregator.Publish(new TestEvent { Message = "Hello from Event Aggregator!" });
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        // Save secrets before shutdown
        var userSecret = _userSecret.GetCurrentValue();

        if (string.IsNullOrEmpty(userSecret)) return Task.CompletedTask;

        _secureStorage.StoreSecret(_userSecret.Key, userSecret);
        _logger.LogDebug("Persisted secret '{Key}' to SecureStorage", _userSecret.Key);

        return Task.CompletedTask;
    }

    private void HandleTestEvent(TestEvent evt)
    {
        _logger.LogInfo("Received TestEvent! Message: '{Message}'", evt.Message);
    }

    /// <summary>
    ///     Releases any resources used by the module.
    /// </summary>
    public void Dispose()
    {
        // Save secrets for extra safety
        var userSecret = _userSecret.GetCurrentValue();
        if (!string.IsNullOrEmpty(userSecret)) _secureStorage.StoreSecret(_userSecret.Key, userSecret);

        _subscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}