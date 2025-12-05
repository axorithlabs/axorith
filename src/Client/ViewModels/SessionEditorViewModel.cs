using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.DependencyInjection;
using PresetSummary = Axorith.Client.CoreSdk.Abstractions.PresetSummary;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public abstract class TriggerViewModel : ReactiveObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string IconKey { get; }
}

public class ScheduleTriggerViewModel(SessionEditorViewModel? parent = null) : TriggerViewModel
{
    public override string Title => "Time Schedule";
    public override string IconKey => "TimerIcon";

    public override string Description
    {
        get
        {
            var days = new List<string>();
            if (RunOnMonday)
            {
                days.Add("Mon");
            }

            if (RunOnTuesday)
            {
                days.Add("Tue");
            }

            if (RunOnWednesday)
            {
                days.Add("Wed");
            }

            if (RunOnThursday)
            {
                days.Add("Thu");
            }

            if (RunOnFriday)
            {
                days.Add("Fri");
            }

            if (RunOnSaturday)
            {
                days.Add("Sat");
            }

            if (RunOnSunday)
            {
                days.Add("Sun");
            }

            var daysStr = days.Count == 7 ? "Every day" : string.Join(", ", days);
            var result = $"{Time:hh\\:mm} • {daysStr}";

            if (!AutoStopDuration.HasValue || AutoStopDuration.Value <= TimeSpan.Zero)
            {
                return result;
            }

            var hours = AutoStopDuration.Value.Hours;
            var minutes = AutoStopDuration.Value.Minutes;
            var durationStr = hours > 0 
                ? $"{hours}h {minutes}m" 
                : $"{minutes}m";
            result += $" • Auto-stop: {durationStr}";
                
            if (NextPresetId.HasValue && !string.IsNullOrWhiteSpace(NextPresetName))
            {
                result += $" → {NextPresetName}";
            }

            return result;
        }
    }

    public TimeSpan Time
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = new(9, 0, 0);

    public bool RunOnMonday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = true;

    public bool RunOnTuesday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = true;

    public bool RunOnWednesday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = true;

    public bool RunOnThursday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = true;

    public bool RunOnFriday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = true;

    public bool RunOnSaturday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    public bool RunOnSunday
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    public Guid? ExistingScheduleId { get; set; }

    public TimeSpan? AutoStopDuration
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    public Guid? NextPresetId
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    public string? NextPresetName
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    private int _autoStopHours;
    public int AutoStopHours
    {
        get => _autoStopHours;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoStopHours, value);
            UpdateAutoStopDuration();
        }
    }

    private int _autoStopMinutes;
    public int AutoStopMinutes
    {
        get => _autoStopMinutes;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoStopMinutes, value);
            UpdateAutoStopDuration();
        }
    }

    private bool _isAutoStopEnabled;
    public bool IsAutoStopEnabled
    {
        get => _isAutoStopEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAutoStopEnabled, value);
            if (!value)
            {
                AutoStopDuration = null;
            }
            else
            {
                UpdateAutoStopDuration();
            }
        }
    }

    private void UpdateAutoStopDuration()
    {
        if (_isAutoStopEnabled && (_autoStopHours > 0 || _autoStopMinutes > 0))
        {
            AutoStopDuration = TimeSpan.FromHours(_autoStopHours) + TimeSpan.FromMinutes(_autoStopMinutes);
        }
        else if (!_isAutoStopEnabled)
        {
            AutoStopDuration = null;
        }
    }

    public string NextActionType
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (value == "Stop session")
            {
                SelectedNextPreset = null;
                NextPresetId = null;
                NextPresetName = null;
            }

            this.RaisePropertyChanged(nameof(IsNextPresetSelectionVisible));
            this.RaisePropertyChanged(nameof(IsNoOtherPresetsAvailable));
        }
    } = "Stop session";

    public bool IsNextPresetSelectionVisible => NextActionType == "Start another session" && (parent?.AvailablePresetsForNext.Count ?? 0) > 0;

    public bool IsNoOtherPresetsAvailable => NextActionType == "Start another session" && (parent?.AvailablePresetsForNext.Count ?? 0) == 0;

    public NextPresetOption? SelectedNextPreset
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            NextPresetId = value?.PresetId;
            NextPresetName = value?.Name;
        }
    }
}

public class NextPresetOption
{
    public Guid? PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SessionEditorViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IModulesApi _modulesApi;
    private readonly IPresetsApi _presetsApi;
    private readonly ISchedulerApi _schedulerApi;
    private readonly IToastNotificationService _toastService;
    private readonly IServiceProvider _serviceProvider;

    private IReadOnlyList<ModuleDefinition> _availableModules = [];
    private SessionPreset _preset = new(id: Guid.NewGuid());
    private readonly ObservableCollection<NextPresetOption> _availablePresetsForNext = [];

    private readonly ObservableAsPropertyHelper<bool> _isFooterVisible;
    public bool IsFooterVisible => _isFooterVisible.Value;

    private readonly ObservableAsPropertyHelper<bool> _canAddAnyTrigger;
    public bool CanAddAnyTrigger => _canAddAnyTrigger.Value;
    
    private readonly ObservableAsPropertyHelper<bool> _hasValidationErrors;
    public bool HasValidationErrors => _hasValidationErrors.Value;

    public SessionPreset? PresetToEdit
    {
        get => _preset;
        set
        {
            _preset = value ?? new SessionPreset { Id = Guid.NewGuid() };
            // Update presets list when preset changes
            if (_availablePresetsForNext.Count > 0)
            {
                UpdateAvailablePresetsForNext();
            }
            LoadFromPreset();
        }
    }

    public string Name
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            Validate();
        }
    } = string.Empty;

    public string? ErrorMessage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<TriggerViewModel> Triggers { get; } = [];
    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = [];

    public ConfiguredModuleViewModel? SelectedModule
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public TriggerViewModel? SelectedTrigger
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ModuleSelectorViewModel? ModuleSelector
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ICommand SaveAndCloseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RemoveModuleCommand { get; }
    public ICommand OpenModuleSettingsCommand { get; }
    public ICommand CloseModuleSettingsCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand OpenAddModuleCommand { get; }

    public ReactiveCommand<Unit, Unit> AddScheduleTriggerCommand { get; }
    public ICommand RemoveTriggerCommand { get; }
    public ICommand EditTriggerCommand { get; }
    public ICommand CloseTriggerSettingsCommand { get; }

    public Task InitializationTask { get; private set; }

    public ObservableCollection<NextPresetOption> AvailablePresetsForNext => _availablePresetsForNext;

    public SessionEditorViewModel(
        ShellViewModel shell,
        IModulesApi modulesApi,
        IPresetsApi presetsApi,
        ISchedulerApi schedulerApi,
        IToastNotificationService toastService,
        IServiceProvider serviceProvider)
    {
        _shell = shell;
        _modulesApi = modulesApi;
        _presetsApi = presetsApi;
        _schedulerApi = schedulerApi;
        _toastService = toastService;
        _serviceProvider = serviceProvider;

        _isFooterVisible = this.WhenAnyValue(x => x.SelectedModule, x => x.ModuleSelector, x => x.SelectedTrigger)
            .Select(t => t.Item1 == null && t.Item2 == null && t.Item3 == null)
            .ToProperty(this, x => x.IsFooterVisible);

        _hasValidationErrors = ConfiguredModules
            .ToObservableChangeSet()
            .AutoRefresh(m => m.HasErrors)
            .ToCollection()
            .Select(modules => modules.Any(m => m.HasErrors))
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.HasValidationErrors);

        var canSave = this.WhenAnyValue(vm => vm.Name, vm => vm.HasValidationErrors)
            .Select(t => !string.IsNullOrWhiteSpace(t.Item1) && !t.Item2);

        SaveAndCloseCommand = ReactiveCommand.CreateFromTask(SaveAndCloseAsync, canSave);
        CancelCommand = ReactiveCommand.Create(Cancel);

        OpenAddModuleCommand = ReactiveCommand.Create(OpenModuleSelector);

        RemoveModuleCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(moduleVm =>
        {
            ConfiguredModules.Remove(moduleVm);
            moduleVm.Dispose();
            if (SelectedModule == moduleVm)
            {
                SelectedModule = null;
            }

            UpdateModuleLinks();
        });

        OpenModuleSettingsCommand =
            ReactiveCommand.Create<ConfiguredModuleViewModel>(moduleVm => { SelectedModule = moduleVm; });
        CloseModuleSettingsCommand = ReactiveCommand.Create(() => { SelectedModule = null; });

        MoveUpCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(vm =>
        {
            var index = ConfiguredModules.IndexOf(vm);
            if (index > 0)
            {
                ConfiguredModules.Move(index, index - 1);
                UpdateModuleLinks();
            }
        });

        MoveDownCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(vm =>
        {
            var index = ConfiguredModules.IndexOf(vm);
            if (index < ConfiguredModules.Count - 1)
            {
                ConfiguredModules.Move(index, index + 1);
                UpdateModuleLinks();
            }
        });

        var canAddSchedule = Triggers
            .ToObservableChangeSet()
            .Select(_ => !Triggers.Any(t => t is ScheduleTriggerViewModel))
            .ObserveOn(RxApp.MainThreadScheduler);

        _canAddAnyTrigger = canAddSchedule.ToProperty(this, x => x.CanAddAnyTrigger);

        AddScheduleTriggerCommand = ReactiveCommand.Create(() =>
        {
            var trigger = new ScheduleTriggerViewModel(this);
            Triggers.Add(trigger);
            SelectedTrigger = trigger;
        }, canAddSchedule);

        RemoveTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t =>
        {
            Triggers.Remove(t);
            if (SelectedTrigger == t)
            {
                SelectedTrigger = null;
            }
        });

        EditTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t => SelectedTrigger = t);
        CloseTriggerSettingsCommand = ReactiveCommand.Create(() => SelectedTrigger = null);

        InitializationTask = InitializeAsync();
    }

    private void Validate()
    {
        ErrorMessage = !string.IsNullOrWhiteSpace(Name) ? string.Empty : "Preset name cannot be empty.";
    }

    private async Task InitializeAsync()
    {
        try
        {
            var modules = await _modulesApi.ListModulesAsync();
            var presets = await _presetsApi.ListPresetsAsync();
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _availableModules = modules;
                UpdateAvailablePresetsForNext(presets);
                LoadFromPreset();
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => 
            { 
                _availableModules = [];
                _availablePresetsForNext.Clear();
            });
        }
    }

    private void UpdateAvailablePresetsForNext(IReadOnlyList<PresetSummary>? presets = null)
    {
        if (presets == null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var loadedPresets = await _presetsApi.ListPresetsAsync();
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateAvailablePresetsForNext(loadedPresets));
                }
                catch
                {
                    // Ignore
                }
            });
            return;
        }

        _availablePresetsForNext.Clear();
        foreach (var preset in presets)
        {
            if (preset.Id != _preset.Id)
            {
                _availablePresetsForNext.Add(new NextPresetOption { PresetId = preset.Id, Name = preset.Name });
            }
        }
        
        foreach (var trigger in Triggers.OfType<ScheduleTriggerViewModel>())
        {
            trigger.RaisePropertyChanged(nameof(trigger.IsNextPresetSelectionVisible));
            trigger.RaisePropertyChanged(nameof(trigger.IsNoOtherPresetsAvailable));
        }
    }

    private void LoadFromPreset()
    {
        Name = _preset.Name;
        foreach (var vm in ConfiguredModules)
        {
            vm.Dispose();
        }

        ConfiguredModules.Clear();
        Triggers.Clear();

        foreach (var configured in _preset.Modules)
        {
            var moduleDef = _availableModules.FirstOrDefault(m => m.Id == configured.ModuleId);
            if (moduleDef != null)
            {
                ConfiguredModules.Add(new ConfiguredModuleViewModel(moduleDef, configured, _modulesApi,
                    _serviceProvider));
            }
        }

        UpdateModuleLinks();

        _ = LoadSchedulesAsync();
    }

    private async Task LoadSchedulesAsync()
    {
        try
        {
            var schedules = await _schedulerApi.ListSchedulesAsync();
            var presetSchedules = schedules.Where(s => s.PresetId == _preset.Id).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var toRemove = Triggers.Where(t => t is ScheduleTriggerViewModel).ToList();
                foreach (var t in toRemove)
                {
                    Triggers.Remove(t);
                }

                foreach (var s in presetSchedules)
                {
                    var trigger = new ScheduleTriggerViewModel(this)
                    {
                        ExistingScheduleId = s.Id,
                        Time = s.RecurringTime ?? TimeSpan.Zero,
                        RunOnMonday = s.DaysOfWeek.Contains(DayOfWeek.Monday),
                        RunOnTuesday = s.DaysOfWeek.Contains(DayOfWeek.Tuesday),
                        RunOnWednesday = s.DaysOfWeek.Contains(DayOfWeek.Wednesday),
                        RunOnThursday = s.DaysOfWeek.Contains(DayOfWeek.Thursday),
                        RunOnFriday = s.DaysOfWeek.Contains(DayOfWeek.Friday),
                        RunOnSaturday = s.DaysOfWeek.Contains(DayOfWeek.Saturday),
                        RunOnSunday = s.DaysOfWeek.Contains(DayOfWeek.Sunday),
                        AutoStopDuration = s.AutoStopDuration,
                        NextPresetId = s.NextPresetId
                    };
                    
                    if (s.AutoStopDuration.HasValue)
                    {
                        trigger.IsAutoStopEnabled = true;
                        trigger.AutoStopHours = s.AutoStopDuration.Value.Hours;
                        trigger.AutoStopMinutes = s.AutoStopDuration.Value.Minutes;
                    }
                    
                    if (s.NextPresetId.HasValue)
                    {
                        trigger.NextActionType = "Start another session";
                        var nextPreset = _availablePresetsForNext.FirstOrDefault(p => p.PresetId == s.NextPresetId.Value);
                        if (nextPreset != null)
                        {
                            trigger.SelectedNextPreset = nextPreset;
                            trigger.NextPresetName = nextPreset.Name;
                        }
                        else
                        {
                            // Preset not found (maybe it's the current one being edited), try to load it
                            _ = LoadNextPresetNameAsync(trigger, s.NextPresetId.Value);
                        }
                    }
                    else
                    {
                        trigger.NextActionType = "Stop session";
                        trigger.SelectedNextPreset = null;
                    }
                    
                    Triggers.Add(trigger);
                }
            });
        }
        catch
        {
            /* Ignore */
        }
    }

    private async Task LoadNextPresetNameAsync(ScheduleTriggerViewModel trigger, Guid presetId)
    {
        try
        {
            var preset = await _presetsApi.GetPresetAsync(presetId);
            if (preset != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    trigger.NextPresetName = preset.Name;
                });
            }
        }
        catch
        {
            /* Ignore */
        }
    }

    private void OpenModuleSelector()
    {
        ModuleSelector = new ModuleSelectorViewModel(_availableModules, OnModuleAdded, () => ModuleSelector = null);
    }

    private void OnModuleAdded(ModuleDefinition defToAdd)
    {
        var newConfiguredModule = new ConfiguredModule { ModuleId = defToAdd.Id };
        var newVm = new ConfiguredModuleViewModel(defToAdd, newConfiguredModule, _modulesApi, _serviceProvider);
        ConfiguredModules.Add(newVm);
        UpdateModuleLinks();
    }

    private async Task SaveAndCloseAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Preset name cannot be empty.";
            return;
        }

        if (HasValidationErrors)
        {
            ErrorMessage = "Please fix configuration errors before saving.";
            return;
        }

        ErrorMessage = string.Empty;
        _preset.Modules = ConfiguredModules.Select(vm =>
        {
            vm.SaveChangesToModel();
            return vm.Model;
        }).ToList();
        _preset.Name = Name;

        try
        {
            var existingPreset = await _presetsApi.GetPresetAsync(_preset.Id);
            if (existingPreset != null)
            {
                await _presetsApi.UpdatePresetAsync(_preset);
            }
            else
            {
                await _presetsApi.CreatePresetAsync(_preset);
            }

            var existingSchedules = await _schedulerApi.ListSchedulesAsync();
            var presetSchedules = existingSchedules.Where(s => s.PresetId == _preset.Id).ToList();

            var activeTriggerIds = new HashSet<Guid>();

            foreach (var trigger in Triggers.OfType<ScheduleTriggerViewModel>())
            {
                var days = new List<DayOfWeek>();
                if (trigger.RunOnMonday)
                {
                    days.Add(DayOfWeek.Monday);
                }

                if (trigger.RunOnTuesday)
                {
                    days.Add(DayOfWeek.Tuesday);
                }

                if (trigger.RunOnWednesday)
                {
                    days.Add(DayOfWeek.Wednesday);
                }

                if (trigger.RunOnThursday)
                {
                    days.Add(DayOfWeek.Thursday);
                }

                if (trigger.RunOnFriday)
                {
                    days.Add(DayOfWeek.Friday);
                }

                if (trigger.RunOnSaturday)
                {
                    days.Add(DayOfWeek.Saturday);
                }

                if (trigger.RunOnSunday)
                {
                    days.Add(DayOfWeek.Sunday);
                }

                var schedule = new SessionSchedule
                {
                    Id = trigger.ExistingScheduleId ?? Guid.NewGuid(),
                    PresetId = _preset.Id,
                    Type = ScheduleType.Recurring,
                    Name = $"{Name} Schedule",
                    IsEnabled = true,
                    RecurringTime = trigger.Time,
                    DaysOfWeek = days,
                    AutoStopDuration = trigger.AutoStopDuration,
                    NextPresetId = trigger.NextActionType == "Start another session" ? trigger.NextPresetId : null
                };

                if (!trigger.ExistingScheduleId.HasValue && presetSchedules.Count > 0)
                {
                    var candidate = presetSchedules.FirstOrDefault(s => !activeTriggerIds.Contains(s.Id));
                    if (candidate != null)
                    {
                        schedule.Id = candidate.Id;
                        trigger.ExistingScheduleId = candidate.Id;
                    }
                }

                if (trigger.ExistingScheduleId.HasValue)
                {
                    await _schedulerApi.UpdateScheduleAsync(schedule);
                }
                else
                {
                    await _schedulerApi.CreateScheduleAsync(schedule);
                }

                activeTriggerIds.Add(schedule.Id);
            }

            foreach (var s in presetSchedules.Where(s => !activeTriggerIds.Contains(s.Id)))
            {
                await _schedulerApi.DeleteScheduleAsync(s.Id);
            }

            _toastService.Show("Preset saved successfully", Sdk.Services.NotificationType.Success);

            Cancel();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
    }

    private void Cancel()
    {
        foreach (var vm in ConfiguredModules)
        {
            try
            {
                vm.Dispose();
            }
            catch
            {
                // Ignore disposal errors to ensure all modules are disposed
            }
        }
        ConfiguredModules.Clear();

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.LoadPresetsCommand.Execute(Unit.Default);
        _shell.NavigateTo(mainViewModel);
    }

    private void UpdateModuleLinks()
    {
        for (var i = 0; i < ConfiguredModules.Count; i++)
        {
            var current = ConfiguredModules[i];
            var next = i < ConfiguredModules.Count - 1 ? ConfiguredModules[i + 1] : null;
            current.NextModule = next;
            current.IsFirst = i == 0;
            current.IsLast = i == ConfiguredModules.Count - 1;
        }
    }
}