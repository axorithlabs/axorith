using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reflection;
using System.Windows.Input;
using Axorith.Client.Services;
using Axorith.Client.Services.Abstractions;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;
using Axorith.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Axorith.Client.ViewModels;

public sealed partial class SettingsViewModel : ReactiveObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IClientUiSettingsStore _settingsStore;
    private readonly IAutoStartManager _autoStartManager;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CompositeDisposable _disposables = [];
    private readonly ClientUiConfiguration _config;
    private readonly IToastNotificationService? _toastService;
    public MainViewModel? MainViewModel { get; }
    public bool CanCheckForUpdates => MainViewModel is not null;

    [Reactive(nameof(IsGeneralSection), nameof(IsPrivacySection), nameof(IsAboutSection))]
    public partial string SelectedSection { get; set; } = "General";

    public bool IsGeneralSection => SelectedSection == "General";
    public bool IsPrivacySection => SelectedSection == "Privacy";
    public bool IsAboutSection => SelectedSection == "About";

    private bool _telemetryEnabled;
    private bool _autoStartEnabled;
    private bool _autoStartMinimized;
    private bool _minimizeToTrayOnClose;

    public bool TelemetryEnabled
    {
        get => _telemetryEnabled;
        set => SetSetting(ref _telemetryEnabled, value, nameof(TelemetryEnabled));
    }

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set => SetSetting(ref _autoStartEnabled, value, nameof(AutoStartEnabled));
    }

    public bool AutoStartMinimized
    {
        get => _autoStartMinimized;
        set => SetSetting(ref _autoStartMinimized, value, nameof(AutoStartMinimized));
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set => SetSetting(ref _minimizeToTrayOnClose, value, nameof(MinimizeToTrayOnClose));
    }

    [Reactive]
    public partial bool HasUnsavedChanges { get; private set; }

    public string AppVersion { get; }

    public ICommand BackCommand { get; }
    public ICommand OpenPresetsCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand OpenPrivacyPolicyCommand { get; }
    public ICommand OpenGitHubCommand { get; }

    public SettingsViewModel(
        ShellViewModel shell,
        IClientUiSettingsStore settingsStore,
        IAutoStartManager autoStartManager,
        ITelemetryService telemetry,
        IOptions<Configuration> options,
        IServiceProvider serviceProvider,
        ILogger<SettingsViewModel> logger)
    {
        _shell = shell;
        MainViewModel = shell.Content as MainViewModel;
        _settingsStore = settingsStore;
        _autoStartManager = autoStartManager;
        _telemetry = telemetry;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = options.Value.Ui;
        _toastService = serviceProvider.GetService<IToastNotificationService>();

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        AppVersion = version is null ? "v0.0.0" : $"v{version.Major}.{version.Minor}.{version.Build}";

        LoadSettings();

        BackCommand = ReactiveCommand.CreateFromTask(() => NavigateToMainAsync(showPresets: false));
        OpenPresetsCommand = ReactiveCommand.CreateFromTask(() => NavigateToMainAsync(showPresets: true));
        SelectSectionCommand = ReactiveCommand.Create<string>(section => SelectedSection = section);
        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://axorith.com/privacy/"));
        OpenGitHubCommand = ReactiveCommand.Create(() => OpenUrl("https://github.com/axorithlabs/axorith"));
    }

    private void LoadSettings()
    {
        _telemetryEnabled = _config.TelemetryEnabled;
        _autoStartEnabled = _autoStartManager.IsAutoStartEnabled;
        _autoStartMinimized = _autoStartManager.IsStartMinimized || _config.AutoStartMinimized;
        _minimizeToTrayOnClose = _config.MinimizeToTrayOnClose;

        this.RaisePropertyChanged(nameof(TelemetryEnabled));
        this.RaisePropertyChanged(nameof(AutoStartEnabled));
        this.RaisePropertyChanged(nameof(AutoStartMinimized));
        this.RaisePropertyChanged(nameof(MinimizeToTrayOnClose));

        HasUnsavedChanges = false;
    }

    [ReactiveCommand]
    private void Save()
    {
        var previous = (_config.TelemetryEnabled, _config.AutoStartEnabled, _config.AutoStartMinimized,
            _config.MinimizeToTrayOnClose);
        try
        {
            List<string> changedCategories = [];
            if (_config.TelemetryEnabled != _telemetryEnabled) changedCategories.Add("telemetry");
            if (_config.AutoStartEnabled != _autoStartEnabled || _config.AutoStartMinimized != _autoStartMinimized)
                changedCategories.Add("startup");
            if (_config.MinimizeToTrayOnClose != _minimizeToTrayOnClose) changedCategories.Add("tray_behavior");

            _config.TelemetryEnabled = _telemetryEnabled;
            _config.AutoStartEnabled = _autoStartEnabled;
            _config.AutoStartMinimized = _autoStartMinimized;
            _config.MinimizeToTrayOnClose = _minimizeToTrayOnClose;

            if (!_settingsStore.Save(_config))
            {
                (_config.TelemetryEnabled, _config.AutoStartEnabled, _config.AutoStartMinimized,
                    _config.MinimizeToTrayOnClose) = previous;
                _toastService?.Show("Could not save settings. Your changes are still open; try again.",
                    NotificationType.Error, "Settings");
                _logger.LogWarning("Settings were not saved; keeping pending changes.");
                return;
            }
            _telemetry.SetEnabled(_telemetryEnabled);

            if (_autoStartEnabled)
                _autoStartManager.EnableAutoStart(_autoStartMinimized);
            else _autoStartManager.DisableAutoStart();

            HasUnsavedChanges = false;

            if (changedCategories.Count > 0)
            {
                _telemetry.TrackEvent("SettingsChanged", new Dictionary<string, object?>
                {
                    ["changedCategories"] = changedCategories
                });
            }

            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    private void SetSetting(ref bool field, bool value, string propertyName)
    {
        this.RaiseAndSetIfChanged(ref field, value, propertyName);
        HasUnsavedChanges = true;
    }

    private async Task NavigateToMainAsync(bool showPresets)
    {
        if (HasUnsavedChanges)
        {
            Save();
            if (HasUnsavedChanges) return;
        }

        var mainViewModel = MainViewModel ?? _serviceProvider.GetRequiredService<MainViewModel>();
        if (showPresets)
            mainViewModel.ShowPresets();
        else mainViewModel.ShowHome();

        await mainViewModel.InitializeAsync();
        _shell.NavigateTo(mainViewModel);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public void Dispose() => _disposables.Dispose();
}
