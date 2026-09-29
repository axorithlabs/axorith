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

namespace Axorith.Client.ViewModels;

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
    private readonly IClientOnboardingService? _onboardingService;
    private readonly IToastNotificationService? _toastService;
    private string _selectedSection = "General";

    public MainViewModel? MainViewModel { get; }
    public bool CanCheckForUpdates => MainViewModel is not null;

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (_selectedSection == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSection, value);
            this.RaisePropertyChanged(nameof(IsGeneralSection));
            this.RaisePropertyChanged(nameof(IsPrivacySection));
            this.RaisePropertyChanged(nameof(IsAboutSection));
        }
    }

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
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string AppVersion { get; }

    public ICommand SaveCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand OpenPresetsCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand OpenPrivacyPolicyCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand RunSetupWizardCommand { get; }

    public bool IsRunningSetup
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

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
        _onboardingService = serviceProvider.GetService<IClientOnboardingService>();
        _toastService = serviceProvider.GetService<IToastNotificationService>();

        AppVersion = GetAppVersion();

        LoadSettings();

        SaveCommand = ReactiveCommand.Create(SaveSettings);
        BackCommand = ReactiveCommand.CreateFromTask(() => NavigateToMainAsync(showPresets: false));
        OpenPresetsCommand = ReactiveCommand.CreateFromTask(() => NavigateToMainAsync(showPresets: true));
        SelectSectionCommand = ReactiveCommand.Create<string>(section => SelectedSection = section);
        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://axorith.com/privacy/"));
        OpenGitHubCommand = ReactiveCommand.Create(() => OpenUrl("https://github.com/axorithlabs/axorith"));

        var canRunSetup = this.WhenAnyValue(x => x.IsRunningSetup, running => !running);
        RunSetupWizardCommand = ReactiveCommand.CreateFromTask(RunSetupWizardAsync, canRunSetup);
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
            var changedCategories = new List<string>();
            if (_config.TelemetryEnabled != _telemetryEnabled) changedCategories.Add("telemetry");
            if (_config.AutoStartEnabled != _autoStartEnabled || _config.AutoStartMinimized != _autoStartMinimized)
                changedCategories.Add("startup");
            if (_config.MinimizeToTrayOnClose != _minimizeToTrayOnClose) changedCategories.Add("tray_behavior");

            _config.TelemetryEnabled = _telemetryEnabled;
            _config.AutoStartEnabled = _autoStartEnabled;
            _config.AutoStartMinimized = _autoStartMinimized;
            _config.MinimizeToTrayOnClose = _minimizeToTrayOnClose;

            var saved = _settingsStore.Save(_config);
            _telemetry.SetEnabled(_telemetryEnabled);
            if (saved && _telemetryEnabled)
            {
                Program.ConfirmPendingInstallation();
            }

            if (_autoStartEnabled)
            {
                _autoStartManager.EnableAutoStart(_autoStartMinimized);
            }
            else
            {
                _autoStartManager.DisableAutoStart();
            }

            HasUnsavedChanges = false;

            if (saved && changedCategories.Count > 0)
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

    private async Task NavigateToMainAsync(bool showPresets)
    {
        if (HasUnsavedChanges)
        {
            SaveSettings();
        }

        var mainViewModel = MainViewModel ?? _serviceProvider.GetRequiredService<MainViewModel>();
        if (showPresets)
        {
            mainViewModel.ShowPresets();
        }
        else
        {
            mainViewModel.ShowHome();
        }

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

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version;
        return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.0.0";
    }

    private async Task RunSetupWizardAsync()
    {
        if (_onboardingService == null)
        {
            return;
        }

        IsRunningSetup = true;

        try
        {
            _telemetry.TrackEvent("OnboardingStarted");
            var result = await _onboardingService.RunSetupAsync();

            if (result.Errors.Count == 0 && result.ErrorMessage is null)
            {
                _telemetry.TrackEvent("OnboardingCompleted", new Dictionary<string, object?>
                {
                    ["createdCount"] = result.CreatedCount
                });
                if (result.CreatedCount > 0)
                {
                    _toastService?.Show(
                        $"Setup complete: Created {result.CreatedCount} preset(s): {string.Join(", ", result.CreatedPresetNames)}",
                        NotificationType.Success);
                }
                else
                {
                    _toastService?.Show("No new presets created. Required modules may not be installed.");
                }
            }
            else
            {
                _telemetry.TrackEvent("OnboardingFailed", new Dictionary<string, object?>
                {
                    ["stage"] = "onboarding"
                });
                _toastService?.Show(
                    $"Setup completed with errors: {result.Errors.FirstOrDefault() ?? result.ErrorMessage}",
                    NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup wizard failed");
            _telemetry.TrackError(ex, "onboarding", "onboarding_create", "error", handled: true, fatal: false);
            _telemetry.TrackEvent("OnboardingFailed", new Dictionary<string, object?>
            {
                ["stage"] = "onboarding"
            });
            _toastService?.Show($"Setup failed: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsRunningSetup = false;
        }
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
