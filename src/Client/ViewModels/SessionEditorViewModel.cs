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
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public abstract class TriggerViewModel : ReactiveObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string IconKey { get; }
}

public class ScheduleTriggerViewModel : TriggerViewModel
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
            return $"{Time:hh\\:mm} • {daysStr}";
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

    // Days

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
            var trigger = new ScheduleTriggerViewModel();
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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _availableModules = modules;
                LoadFromPreset();
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => { _availableModules = []; });
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
                    var trigger = new ScheduleTriggerViewModel
                    {
                        ExistingScheduleId = s.Id,
                        Time = s.RecurringTime ?? TimeSpan.Zero,
                        RunOnMonday = s.DaysOfWeek.Contains(DayOfWeek.Monday),
                        RunOnTuesday = s.DaysOfWeek.Contains(DayOfWeek.Tuesday),
                        RunOnWednesday = s.DaysOfWeek.Contains(DayOfWeek.Wednesday),
                        RunOnThursday = s.DaysOfWeek.Contains(DayOfWeek.Thursday),
                        RunOnFriday = s.DaysOfWeek.Contains(DayOfWeek.Friday),
                        RunOnSaturday = s.DaysOfWeek.Contains(DayOfWeek.Saturday),
                        RunOnSunday = s.DaysOfWeek.Contains(DayOfWeek.Sunday)
                    };
                    Triggers.Add(trigger);
                }
            });
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
                    DaysOfWeek = days
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