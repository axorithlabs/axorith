using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Threading;
using Axorith.Client.Adapters;
using Axorith.Client.CoreSdk;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for a module instance configured within a session preset.
///     Manages settings and actions retrieved from the Host via gRPC API.
/// </summary>
public class ConfiguredModuleViewModel : ReactiveObject, IDisposable
{
    private readonly IModulesApi _modulesApi;
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

    public bool IsLoading
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];
    public ObservableCollection<ActionViewModel> Actions { get; } = [];

    public ConfiguredModuleViewModel(ModuleDefinition definition, ConfiguredModule model, IModulesApi modulesApi)
    {
        Definition = definition;
        Model = model;
        _modulesApi = modulesApi;

        // Subscribe to reactive setting updates from running modules
        // NOTE: Reactive updates (like visibility changes) only work when session is ACTIVE
        // In design-time (no session), only value changes are saved, not reactive UI updates
        _settingUpdatesSubscription = _modulesApi.SettingUpdates
            .Where(update => update.ModuleInstanceId == Model.InstanceId)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(HandleSettingUpdate);

        _settingStreamHandle = _modulesApi.SubscribeToSettingUpdates(Model.InstanceId);

        _ = LoadSettingsAndActionsAsync();
    }

    private async Task LoadSettingsAndActionsAsync()
    {
        try
        {
            IsLoading = true;
            // Start design-time sandbox FIRST to avoid race with UpdateSetting
            var initialSnapshot = new Dictionary<string, object?>();
            foreach (var kv in Model.Settings)
                initialSnapshot[kv.Key] = kv.Value;
            try
            {
                await _modulesApi.BeginEditAsync(Definition.Id, Model.InstanceId, initialSnapshot)
                    .ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            var settingsInfo = await _modulesApi.GetModuleSettingsAsync(Definition.Id).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Settings.Clear();
                Actions.Clear();

                foreach (var setting in settingsInfo.Settings)
                {
                    var savedValue = Model.Settings.GetValueOrDefault(setting.Key);
                    var adaptedSetting = new ModuleSettingAdapter(setting, savedValue);
                    var vm = new SettingViewModel(adaptedSetting, Model.InstanceId, _modulesApi);
                    Settings.Add(vm);
                }

                foreach (var action in settingsInfo.Actions)
                {
                    var adaptedAction = new ModuleActionAdapter(action, _modulesApi, Definition.Id);
                    Actions.Add(new ActionViewModel(adaptedAction));
                }
            });

            // Sandbox is already ensured above; now request Host to re-broadcast current reactive state
            // so that visibility/labels/readonly are applied after VMs are created
            try
            {
                await _modulesApi.SyncEditAsync(Model.InstanceId).ConfigureAwait(false);
            }
            catch
            {
                // Ignore sync errors to avoid breaking UI
            }
        }
        catch (Exception)
        {
            // Log error but don't crash - UI will show empty settings
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void HandleSettingUpdate(SettingUpdate update)
    {
        var settingVm = Settings.FirstOrDefault(s => s.Setting.Key == update.SettingKey);

        if (settingVm?.Setting is not ModuleSettingAdapter adapter)
            return;

        switch (update.Property)
        {
            case SettingProperty.Value:
                adapter.SetValueFromString(update.Value?.ToString());
                break;

            case SettingProperty.Label:
                if (update.Value is string labelValue)
                    adapter.SetLabel(labelValue);
                break;

            case SettingProperty.Visibility:
                if (update.Value is bool visibilityValue)
                    adapter.SetVisibility(visibilityValue);
                break;

            case SettingProperty.ReadOnly:
                if (update.Value is bool readOnlyValue)
                    adapter.SetReadOnly(readOnlyValue);
                break;

            case SettingProperty.Choices:
                if (update.Value is IReadOnlyList<KeyValuePair<string, string>> choicesValue)
                    adapter.SetChoices(choicesValue);
                break;
        }
    }

    public void SaveChangesToModel()
    {
        foreach (var settingVm in Settings)
        {
            if (settingVm.Setting.Persistence != SettingPersistence.Persisted)
                continue;

            Model.Settings[settingVm.Setting.Key] = settingVm.Setting.GetValueAsString();
        }
    }

    public void Dispose()
    {
        _settingUpdatesSubscription.Dispose();
        _settingStreamHandle?.Dispose();

        foreach (var setting in Settings)
            setting.Dispose();
        Settings.Clear();

        foreach (var action in Actions)
            action.Dispose();
        Actions.Clear();

        try
        {
            _ = _modulesApi.EndEditAsync(Model.InstanceId);
        }
        catch
        {
            /* ignore */
        }

        GC.SuppressFinalize(this);
    }
}