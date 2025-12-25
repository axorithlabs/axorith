using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Client.Services.Abstractions;
using Axorith.Shared.Platform;
using Axorith.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for the application settings view.
/// </summary>
public sealed class SettingsViewModel : ReactiveObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IClientUiSettingsStore _settingsStore;
    private readonly IAutoStartManager _autoStartManager;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CompositeDisposable _disposables = [];
    private readonly ClientUiConfiguration _config;

    private bool _telemetryEnabled;
    private bool _autoStartEnabled;
    private bool _autoStartMinimized;
    private bool _minimizeToTrayOnClose;
    private bool _hasUnsavedChanges;

    public bool TelemetryEnabled
    {
        get => _telemetryEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _telemetryEnabled, value);
            HasUnsavedChanges = true;
        }
    }

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoStartEnabled, value);
            HasUnsavedChanges = true;
        }
    }

    public bool AutoStartMinimized
    {
        get => _autoStartMinimized;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoStartMinimized, value);
            HasUnsavedChanges = true;
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            this.RaiseAndSetIfChanged(ref _minimizeToTrayOnClose, value);
            HasUnsavedChanges = true;
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
    }

    public string AppVersion { get; }

    public ICommand SaveCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand OpenPrivacyPolicyCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenDiscordCommand { get; }

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
        _settingsStore = settingsStore;
        _autoStartManager = autoStartManager;
        _telemetry = telemetry;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = options.Value.Ui;

        AppVersion = GetAppVersion();

        LoadSettings();

        var canSave = this.WhenAnyValue(x => x.HasUnsavedChanges);

        SaveCommand = ReactiveCommand.Create(SaveSettings, canSave);
        BackCommand = ReactiveCommand.Create(NavigateBack);
        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://axorith.com/privacy"));
        OpenGitHubCommand = ReactiveCommand.Create(() => OpenUrl("https://github.com/axorithlabs/axorith"));
        OpenDiscordCommand = ReactiveCommand.Create(() => OpenUrl("https://discord.gg/axorith"));

        this.WhenAnyValue(
                x => x.TelemetryEnabled,
                x => x.AutoStartEnabled,
                x => x.AutoStartMinimized,
                x => x.MinimizeToTrayOnClose)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => SaveSettings())
            .DisposeWith(_disposables);
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

    private void SaveSettings()
    {
        try
        {
            _config.TelemetryEnabled = _telemetryEnabled;
            _config.AutoStartEnabled = _autoStartEnabled;
            _config.AutoStartMinimized = _autoStartMinimized;
            _config.MinimizeToTrayOnClose = _minimizeToTrayOnClose;

            _settingsStore.Save(_config);

            if (_autoStartEnabled)
            {
                _autoStartManager.EnableAutoStart(_autoStartMinimized);
            }
            else
            {
                _autoStartManager.DisableAutoStart();
            }

            HasUnsavedChanges = false;

            _telemetry.TrackEvent("SettingsSaved", new Dictionary<string, object?>
            {
                ["telemetryEnabled"] = _telemetryEnabled,
                ["autoStartEnabled"] = _autoStartEnabled,
                ["autoStartMinimized"] = _autoStartMinimized,
                ["minimizeToTray"] = _minimizeToTrayOnClose
            });

            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    private async void NavigateBack()
    {
        if (HasUnsavedChanges)
        {
            SaveSettings();
        }

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
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
            // Ignore if browser fails to open
        }
    }

    private static string GetAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.0.0";
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

internal static class SettingsViewModelExtensions
{
    public static T DisposeWith<T>(this T disposable, CompositeDisposable compositeDisposable)
        where T : IDisposable
    {
        compositeDisposable.Add(disposable);
        return disposable;
    }
}
