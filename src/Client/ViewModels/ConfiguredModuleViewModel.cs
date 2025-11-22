using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using Axorith.Client.Adapters;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ConfiguredModuleViewModel : ReactiveObject, IDisposable
{
    private readonly IModulesApi _modulesApi;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDisposable _settingUpdatesSubscription;
    private readonly IDisposable? _settingStreamHandle;

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

        _settingUpdatesSubscription = _modulesApi.SettingUpdates
            .Where(update => update.ModuleInstanceId == Model.InstanceId)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleSettingUpdate);

        StartDelaySecondsString = model.StartDelay.TotalSeconds.ToString("G29");

        _settingStreamHandle = _modulesApi.SubscribeToSettingUpdates(Model.InstanceId);

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
        try
        {
            IsLoading = true;
            var initialSnapshot = new Dictionary<string, object?>();
            foreach (var kv in Model.Settings)
            {
                initialSnapshot[kv.Key] = kv.Value;
            }

            await _modulesApi.BeginEditAsync(Definition.Id, Model.InstanceId, initialSnapshot)
                .ConfigureAwait(false);

            var settingsInfo = await _modulesApi.GetModuleSettingsAsync(Definition.Id).ConfigureAwait(false);

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
                    var adaptedAction = new ModuleActionAdapter(action, _modulesApi, Model.InstanceId);
                    Actions.Add(new ActionViewModel(adaptedAction));
                }
            });

            try
            {
                await _modulesApi.SyncEditAsync(Model.InstanceId).ConfigureAwait(false);
            }
            catch
            {
                /* Ignore sync errors */
            }
        }
        catch (Exception)
        {
            /* Log error */
        }
        finally
        {
            IsLoading = false;
        }
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