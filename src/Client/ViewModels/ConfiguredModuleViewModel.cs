using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using Axorith.Client.Adapters;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Telemetry;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ConfiguredModuleViewModel : ReactiveObject, IDisposable
{
    private readonly IModulesApi _modulesApi;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDisposable _settingUpdatesSubscription;
    private readonly ILogger<ConfiguredModuleViewModel>? _logger;
    private readonly ITelemetryService? _telemetry;
    private IDisposable? _settingStreamHandle;
    private IDisposable? _validationSubscription;

    public ModuleDefinition Definition { get; }
    public ConfiguredModule Model { get; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Model.CustomName) ? Model.CustomName : Definition.Name;

    public string? CustomName
    {
        get => Model.CustomName;
        set
        {
            Model.CustomName = value;
            this.RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public string StartDelaySecondsString
    {
        get;
        set
        {
            if (!double.TryParse(value, out var seconds) && !string.IsNullOrEmpty(value))
            {
                this.RaisePropertyChanged();
                return;
            }

            this.RaiseAndSetIfChanged(ref field, value);

            Model.StartDelay = TimeSpan.FromSeconds(Math.Max(0, seconds));
            this.RaisePropertyChanged(nameof(HasDelay));
        }
    }

    public bool HasDelay => Model.StartDelay > TimeSpan.Zero;

    public ConfiguredModuleViewModel? NextModule
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsFirst
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsLast
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsLoading
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasErrors
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];
    public ObservableCollection<ActionViewModel> Actions { get; } = [];

    // Commands for UI interaction in the workflow graph
    public ReactiveCommand<Unit, Unit> AddDelayCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveDelayCommand { get; }

    public ConfiguredModuleViewModel(
        ModuleDefinition definition,
        ConfiguredModule model,
        IModulesApi modulesApi,
        IServiceProvider serviceProvider)
    {
        Definition = definition;
        Model = model;
        _modulesApi = modulesApi;
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<ConfiguredModuleViewModel>>();
        _telemetry = serviceProvider.GetService<ITelemetryService>();

        _settingUpdatesSubscription = _modulesApi.SettingUpdates
            .Where(update => update.ModuleInstanceId == Model.InstanceId)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleSettingUpdate);

        StartDelaySecondsString = model.StartDelay.TotalSeconds.ToString("G29");

        AddDelayCommand = ReactiveCommand.Create(AddDelay);
        RemoveDelayCommand = ReactiveCommand.Create(RemoveDelay);

        _ = LoadSettingsAndActionsAsync();
    }

    private void AddDelay()
    {
        NextModule?.StartDelaySecondsString = "1";
    }

    private void RemoveDelay()
    {
        NextModule?.StartDelaySecondsString = "0";
    }

    private async Task LoadSettingsAndActionsAsync()
    {
        IDisposable? streamHandle = null;
        try
        {
            IsLoading = true;

            var initialSnapshot = new Dictionary<string, object?>();
            foreach (var kv in Model.Settings)
            {
                initialSnapshot[kv.Key] = kv.Value;
            }

            var streamTask = _modulesApi.SubscribeToSettingUpdatesAsync(Model.InstanceId);
            var beginTask = _modulesApi.BeginEditAsync(Definition.Id, Model.InstanceId, initialSnapshot);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
            BeginEditResult? beginResult = null;

            var completed = await Task.WhenAny(beginTask, timeoutTask).ConfigureAwait(false);
            if (completed == beginTask)
            {
                beginResult = await beginTask.ConfigureAwait(false);
            }
            else
            {
                var cached = _modulesApi.GetCachedSettings(Definition.Id);
                if (cached != null)
                {
                    beginResult = new BeginEditResult(
                        cached,
                        new OperationResult(true, "Used cached module settings"));
                }
            }

            if (beginResult == null)
            {
                // Do not block indefinitely: surface a degraded result and observe completion separately
                _ = beginTask.ContinueWith(_ => { /* swallow */ }, TaskContinuationOptions.ExecuteSynchronously);
                beginResult = new BeginEditResult(
                    new ModuleSettingsInfo([], []),
                    new OperationResult(false, "BeginEdit timed out; using empty settings"));
            }

            streamHandle = await streamTask.ConfigureAwait(false);
            _settingStreamHandle = streamHandle;

            var settingsInfo = beginResult.SettingsInfo;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Settings.Clear();
                Actions.Clear();

                foreach (var setting in settingsInfo.Settings)
                {
                    var savedValue = Model.Settings.GetValueOrDefault(setting.Key);
                    var adaptedSetting = new ModuleSettingAdapter(setting, savedValue);

                    var vm = new SettingViewModel(adaptedSetting, Model.InstanceId, _modulesApi, _serviceProvider);
                    Settings.Add(vm);
                }

                foreach (var action in settingsInfo.Actions)
                {
                    var adaptedAction = new ModuleActionAdapter(
                        action,
                        _modulesApi,
                        Definition.Id,
                        Model.InstanceId,
                        Definition.Name,
                        _telemetry);
                    Actions.Add(new ActionViewModel(adaptedAction));
                }

                SetupValidation();
            });

            await ValidateAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Failed to load settings/actions for module {ModuleId} instance {InstanceId}",
                Definition.Id, Model.InstanceId);

            if (streamHandle != null)
            {
                try
                {
                    streamHandle.Dispose();
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetupValidation()
    {
        _validationSubscription?.Dispose();

        var changes = Settings.Select(s => s.ValueChanged).Merge();

        _validationSubscription = changes
            .Throttle(TimeSpan.FromMilliseconds(300)) // Debounce validation requests
            .ObserveOn(RxApp.MainThreadScheduler)
            .SelectMany(_ => ValidateAsync())
            .Subscribe();
    }

    private async Task<Unit> ValidateAsync()
    {
        try
        {
            var snapshot = new Dictionary<string, object?>();
            foreach (var setting in Settings)
            {
                snapshot[setting.Setting.Key] = setting.Setting.GetCurrentValueAsObject();
            }

            var result = await _modulesApi.ValidateSettingsAsync(Definition.Id, Model.InstanceId, snapshot);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var hasErrors = result.Status == ValidationStatus.Error;
                HasErrors = hasErrors;

                foreach (var setting in Settings)
                {
                    setting.Error = null;
                }

                if (!hasErrors || result.FieldErrors.Count <= 0)
                {
                    return;
                }

                {
                    foreach (var (key, error) in result.FieldErrors)
                    {
                        var setting = Settings.FirstOrDefault(s => s.Setting.Key == key);
                        setting?.Error = error;
                    }
                }
                // If global error but no field errors, maybe show on module level?
                // For now, we rely on HasErrors flag which parent VM can use.
            });
        }
        catch
        {
            // Ignore validation errors (e.g. network issues)
        }

        return Unit.Default;
    }

    private void HandleSettingUpdate(SettingUpdate update)
    {
        var settingVm = Settings.FirstOrDefault(s => s.Setting.Key == update.SettingKey);
        if (settingVm?.Setting is ModuleSettingAdapter settingAdapter)
        {
            switch (update.Property)
            {
                case SettingProperty.Value:
                    settingAdapter.SetValueFromString(update.Value?.ToString());
                    break;
                case SettingProperty.Label:
                    if (update.Value is string l)
                    {
                        settingAdapter.SetLabel(l);
                    }

                    break;
                case SettingProperty.Visibility:
                    if (update.Value is bool v)
                    {
                        settingAdapter.SetVisibility(v);
                    }

                    break;
                case SettingProperty.ReadOnly:
                    if (update.Value is bool r)
                    {
                        settingAdapter.SetReadOnly(r);
                    }

                    break;
                case SettingProperty.Choices:
                    if (update.Value is IReadOnlyList<KeyValuePair<string, string>> c)
                    {
                        settingAdapter.SetChoices(c);
                    }

                    break;
            }

            return;
        }

        var actionVm = Actions.FirstOrDefault(a => a.Key == update.SettingKey);
        if (actionVm?.SourceAction is not ModuleActionAdapter actionAdapter)
        {
            return;
        }

        switch (update.Property)
        {
            case SettingProperty.ActionEnabled:
                if (update.Value is bool enabled)
                {
                    actionAdapter.SetEnabled(enabled);
                }

                break;
            case SettingProperty.ActionLabel:
                if (update.Value is string label)
                {
                    actionAdapter.SetLabel(label);
                }

                break;
        }
    }

    public void SaveChangesToModel()
    {
        foreach (var settingVm in Settings)
        {
            if (settingVm.Setting.Persistence != SettingPersistence.Persisted)
            {
                continue;
            }

            Model.Settings[settingVm.Setting.Key] = settingVm.Setting.GetValueAsString();
        }
    }

    public void Dispose()
    {
        _settingUpdatesSubscription.Dispose();
        _settingStreamHandle?.Dispose();
        _validationSubscription?.Dispose();

        foreach (var setting in Settings)
        {
            setting.Dispose();
        }

        Settings.Clear();

        foreach (var action in Actions)
        {
            action.Dispose();
        }

        Actions.Clear();

        _ = _modulesApi.EndEditAsync(Model.InstanceId);

        GC.SuppressFinalize(this);
    }
}