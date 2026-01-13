using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using Axorith.Telemetry;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using PresetSummary = Axorith.Client.CoreSdk.Abstractions.PresetSummary;

namespace Axorith.Client.ViewModels;

public abstract class TriggerViewModel : ReactiveObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string IconKey { get; }
    public virtual bool HasError => false;
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
            var timeStr = Use24HourFormat 
                ? $"{Time:hh\\:mm}" 
                : FormatTime12Hour(Time);
            return $"{timeStr} • {daysStr}";
        }
    }

    private static string FormatTime12Hour(TimeSpan time)
    {
        var hours = time.Hours;
        var minutes = time.Minutes;
        var period = hours >= 12 ? "PM" : "AM";
        var displayHours = hours % 12;
        if (displayHours == 0) displayHours = 12;
        return $"{displayHours}:{minutes:D2} {period}";
    }

    public TimeSpan Time
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateTimeInputs();
            this.RaisePropertyChanged(nameof(Description));
        }
    } = new(9, 0, 0);

    private decimal? _hours = 9;
    public decimal? Hours
    {
        get => _hours;
        set
        {
            if (_hours != value)
            {
                this.RaiseAndSetIfChanged(ref _hours, value);
                this.RaisePropertyChanged(nameof(HasTimeError));
                if (value.HasValue)
                {
                    UpdateTimeFromInputs();
                }
            }
        }
    }

    private decimal? _minutes = 0;
    public decimal? Minutes
    {
        get => _minutes;
        set
        {
            if (_minutes != value)
            {
                this.RaiseAndSetIfChanged(ref _minutes, value);
                this.RaisePropertyChanged(nameof(HasTimeError));
                if (value.HasValue)
                {
                    UpdateTimeFromInputs();
                }
            }
        }
    }

    public bool HasTimeError => !_hours.HasValue || !_minutes.HasValue;

    private bool _isAm = true;
    public bool IsAm
    {
        get => _isAm;
        set
        {
            if (_isAm != value)
            {
                this.RaiseAndSetIfChanged(ref _isAm, value);
                UpdateTimeFromInputs();
            }
        }
    }

    public bool Use24HourFormat
    {
        get;
        set
        {
            if (field != value)
            {
                this.RaiseAndSetIfChanged(ref field, value);
                UpdateTimeInputs();
                this.RaisePropertyChanged(nameof(Description));
            }
        }
    } = true;

    private bool _isUpdatingTime;

    private void UpdateTimeInputs()
    {
        if (_isUpdatingTime) return;
        _isUpdatingTime = true;
        try
        {
            if (Use24HourFormat)
            {
                _hours = Time.Hours;
            }
            else
            {
                var hours = Time.Hours;
                _isAm = hours < 12;
                var displayHours = hours % 12;
                _hours = displayHours == 0 ? 12 : displayHours;
            }
            _minutes = Time.Minutes;
            this.RaisePropertyChanged(nameof(Hours));
            this.RaisePropertyChanged(nameof(Minutes));
            this.RaisePropertyChanged(nameof(IsAm));
            this.RaisePropertyChanged(nameof(HasTimeError));
        }
        finally
        {
            _isUpdatingTime = false;
        }
    }

    private void UpdateTimeFromInputs()
    {
        if (_isUpdatingTime) return;
        if (!_hours.HasValue || !_minutes.HasValue) return;
        
        _isUpdatingTime = true;
        try
        {
            int hours;
            if (Use24HourFormat)
            {
                hours = (int)_hours.Value;
            }
            else
            {
                hours = (int)_hours.Value % 12;
                if (!_isAm) hours += 12;
            }
            Time = new TimeSpan(hours, (int)_minutes.Value, 0);
        }
        finally
        {
            _isUpdatingTime = false;
        }
    }

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

public class NextPresetOption
{
    public Guid? PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class StopAtTimeTriggerViewModel : TriggerViewModel
{
    public override string Title => "Fixed Time";
    public override string IconKey => "TimerIcon";

    public override string Description
    {
        get
        {
            var days = new List<string>();
            if (RunOnMonday) days.Add("Mon");
            if (RunOnTuesday) days.Add("Tue");
            if (RunOnWednesday) days.Add("Wed");
            if (RunOnThursday) days.Add("Thu");
            if (RunOnFriday) days.Add("Fri");
            if (RunOnSaturday) days.Add("Sat");
            if (RunOnSunday) days.Add("Sun");

            var daysStr = days.Count == 7 ? "Every day" : string.Join(", ", days);
            var timeStr = Use24HourFormat 
                ? $"{Time:hh\\:mm}" 
                : FormatTime12Hour(Time);
            return $"{timeStr} • {daysStr}";
        }
    }

    private static string FormatTime12Hour(TimeSpan time)
    {
        var hours = time.Hours;
        var minutes = time.Minutes;
        var period = hours >= 12 ? "PM" : "AM";
        var displayHours = hours % 12;
        if (displayHours == 0) displayHours = 12;
        return $"{displayHours}:{minutes:D2} {period}";
    }

    public TimeSpan Time
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateTimeInputs();
            this.RaisePropertyChanged(nameof(Description));
        }
    } = new(17, 0, 0);

    private decimal? _hours = 17;
    public decimal? Hours
    {
        get => _hours;
        set
        {
            if (_hours != value)
            {
                this.RaiseAndSetIfChanged(ref _hours, value);
                this.RaisePropertyChanged(nameof(HasTimeError));
                if (value.HasValue)
                {
                    UpdateTimeFromInputs();
                }
            }
        }
    }

    private decimal? _minutes = 0;
    public decimal? Minutes
    {
        get => _minutes;
        set
        {
            if (_minutes != value)
            {
                this.RaiseAndSetIfChanged(ref _minutes, value);
                this.RaisePropertyChanged(nameof(HasTimeError));
                if (value.HasValue)
                {
                    UpdateTimeFromInputs();
                }
            }
        }
    }

    public bool HasTimeError => !_hours.HasValue || !_minutes.HasValue;

    private bool _isAm;
    public bool IsAm
    {
        get => _isAm;
        set
        {
            if (_isAm != value)
            {
                this.RaiseAndSetIfChanged(ref _isAm, value);
                UpdateTimeFromInputs();
            }
        }
    }

    public bool Use24HourFormat
    {
        get;
        set
        {
            if (field != value)
            {
                this.RaiseAndSetIfChanged(ref field, value);
                UpdateTimeInputs();
                this.RaisePropertyChanged(nameof(Description));
            }
        }
    } = true;

    private bool _isUpdatingTime;

    private void UpdateTimeInputs()
    {
        if (_isUpdatingTime) return;
        _isUpdatingTime = true;
        try
        {
            if (Use24HourFormat)
            {
                _hours = Time.Hours;
            }
            else
            {
                var hours = Time.Hours;
                _isAm = hours < 12;
                var displayHours = hours % 12;
                _hours = displayHours == 0 ? 12 : displayHours;
            }
            _minutes = Time.Minutes;
            this.RaisePropertyChanged(nameof(Hours));
            this.RaisePropertyChanged(nameof(Minutes));
            this.RaisePropertyChanged(nameof(IsAm));
            this.RaisePropertyChanged(nameof(HasTimeError));
        }
        finally
        {
            _isUpdatingTime = false;
        }
    }

    private void UpdateTimeFromInputs()
    {
        if (_isUpdatingTime) return;
        if (!_hours.HasValue || !_minutes.HasValue) return;
        
        _isUpdatingTime = true;
        try
        {
            int hours;
            if (Use24HourFormat)
            {
                hours = (int)_hours.Value;
            }
            else
            {
                hours = (int)_hours.Value % 12;
                if (!_isAm) hours += 12;
            }
            Time = new TimeSpan(hours, (int)_minutes.Value, 0);
        }
        finally
        {
            _isUpdatingTime = false;
        }
    }

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

public class StopAfterDurationTriggerViewModel : TriggerViewModel
{
    public override string Title => "Session Duration";
    public override string IconKey => "TimerIcon";

    public override string Description
    {
        get
        {
            var durationStr = DurationHours > 0
                ? $"{DurationHours}h {DurationMinutes}m"
                : $"{DurationMinutes}m";
            return $"After {durationStr}";
        }
    }

    public Guid? ExistingScheduleId { get; set; }

    public TimeSpan Duration
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(Description));
        }
    } = TimeSpan.FromHours(1);

    private decimal? _durationHours = 1;
    public decimal? DurationHours
    {
        get => _durationHours;
        set
        {
            this.RaiseAndSetIfChanged(ref _durationHours, value);
            this.RaisePropertyChanged(nameof(HasDurationError));
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(Description));
            UpdateDuration();
        }
    }

    private decimal? _durationMinutes = 0;
    public decimal? DurationMinutes
    {
        get => _durationMinutes;
        set
        {
            this.RaiseAndSetIfChanged(ref _durationMinutes, value);
            this.RaisePropertyChanged(nameof(HasDurationError));
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(Description));
            UpdateDuration();
        }
    }

    public bool HasDurationError => !_durationHours.HasValue || !_durationMinutes.HasValue ||
                                    ((int)(_durationHours ?? 0) == 0 && (int)(_durationMinutes ?? 0) == 0);

    public override bool HasError => HasDurationError;

    private void UpdateDuration()
    {
        if (_durationHours.HasValue && _durationMinutes.HasValue)
        {
            Duration = TimeSpan.FromHours((int)_durationHours.Value) + TimeSpan.FromMinutes((int)_durationMinutes.Value);
        }
    }
}

public class ThenStartAnotherTriggerViewModel(SessionEditorViewModel? parent = null) : TriggerViewModel
{
    public override string Title => "Start Another Session";
    public override string IconKey => "PlayIcon";

    public override string Description
    {
        get
        {
            if (NextPresetId.HasValue && !string.IsNullOrWhiteSpace(NextPresetName))
            {
                return $"Start '{NextPresetName}'";
            }
            return "Select a session to start";
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

    public bool IsNextPresetSelectionVisible => (parent?.AvailablePresetsForNext.Count ?? 0) > 0;

    public bool IsNoOtherPresetsAvailable => (parent?.AvailablePresetsForNext.Count ?? 0) == 0;

    public NextPresetOption? SelectedNextPreset
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            NextPresetId = value?.PresetId;
            NextPresetName = value?.Name;
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public override bool HasError => !NextPresetId.HasValue;

    public void RefreshPresetVisibility()
    {
        this.RaisePropertyChanged(nameof(IsNextPresetSelectionVisible));
        this.RaisePropertyChanged(nameof(IsNoOtherPresetsAvailable));
        this.RaisePropertyChanged(nameof(HasError));
    }
}

public class SessionEditorViewModel : ReactiveObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IModulesApi _modulesApi;
    private readonly IPresetsApi _presetsApi;
    private readonly ISchedulerApi _schedulerApi;
    private readonly IToastNotificationService _toastService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryService? _telemetry;

    private IReadOnlyList<ModuleDefinition> _availableModules = [];
    private SessionPreset _preset = new(id: Guid.NewGuid());
    private bool _disposed;

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
            if (AvailablePresetsForNext.Count > 0)
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
    public ObservableCollection<TriggerViewModel> StopTriggers { get; } = [];
    public ObservableCollection<TriggerViewModel> ThenTriggers { get; } = [];
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

    public TriggerViewModel? SelectedStopTrigger
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public TriggerViewModel? SelectedThenTrigger
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

    public ReactiveCommand<Unit, Unit> AddStopAtTimeTriggerCommand { get; }
    public ReactiveCommand<Unit, Unit> AddStopAfterDurationTriggerCommand { get; }
    public ICommand RemoveStopTriggerCommand { get; }
    public ICommand EditStopTriggerCommand { get; }
    public ICommand CloseStopTriggerSettingsCommand { get; }

    private readonly ObservableAsPropertyHelper<bool> _canAddStopAtTimeTrigger;
    public bool CanAddStopAtTimeTrigger => _canAddStopAtTimeTrigger.Value;

    private readonly ObservableAsPropertyHelper<bool> _canAddStopAfterDurationTrigger;
    public bool CanAddStopAfterDurationTrigger => _canAddStopAfterDurationTrigger.Value;

    public ReactiveCommand<Unit, Unit> AddThenStartAnotherTriggerCommand { get; }
    public ICommand RemoveThenTriggerCommand { get; }
    public ICommand EditThenTriggerCommand { get; }
    public ICommand CloseThenTriggerSettingsCommand { get; }

    private readonly ObservableAsPropertyHelper<bool> _canAddThenStartAnotherTrigger;
    public bool CanAddThenStartAnotherTrigger => _canAddThenStartAnotherTrigger.Value;

    public Task InitializationTask { get; private set; }

    public ObservableCollection<NextPresetOption> AvailablePresetsForNext { get; } = [];

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
        _telemetry = serviceProvider.GetService<ITelemetryService>();

        _isFooterVisible = this.WhenAnyValue(x => x.SelectedModule, x => x.ModuleSelector, x => x.SelectedTrigger, x => x.SelectedStopTrigger, x => x.SelectedThenTrigger)
            .Select(t => t.Item1 == null && t.Item2 == null && t.Item3 == null && t.Item4 == null && t.Item5 == null)
            .ToProperty(this, x => x.IsFooterVisible);

        _hasValidationErrors = ConfiguredModules
            .ToObservableChangeSet()
            .AutoRefresh(m => m.HasErrors)
            .ToCollection()
            .Select(modules => modules.Any(m => m.HasErrors))
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.HasValidationErrors);

        var thenTriggersHasError = ThenTriggers
            .ToObservableChangeSet()
            .AutoRefresh(t => t.HasError)
            .ToCollection()
            .Select(triggers => triggers.Any(t => t.HasError))
            .StartWith(false);

        var stopTriggersHasError = StopTriggers
            .ToObservableChangeSet()
            .AutoRefresh(t => t.HasError)
            .ToCollection()
            .Select(triggers => triggers.Any(t => t.HasError))
            .StartWith(false);

        var canSave = this.WhenAnyValue(vm => vm.Name).CombineLatest(this.WhenAnyValue(vm => vm.HasValidationErrors),
            thenTriggersHasError,
            stopTriggersHasError,
            (name, hasModuleErrors, hasThenErrors, hasStopErrors) => 
                !string.IsNullOrWhiteSpace(name) && !hasModuleErrors && !hasThenErrors && !hasStopErrors);

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

        var canAddStopAtTime = StopTriggers
            .ToObservableChangeSet()
            .Select(_ => !StopTriggers.Any(t => t is StopAtTimeTriggerViewModel))
            .ObserveOn(RxApp.MainThreadScheduler);

        _canAddStopAtTimeTrigger = canAddStopAtTime.ToProperty(this, x => x.CanAddStopAtTimeTrigger);

        AddStopAtTimeTriggerCommand = ReactiveCommand.Create(() =>
        {
            var trigger = new StopAtTimeTriggerViewModel();
            StopTriggers.Add(trigger);
            SelectedStopTrigger = trigger;
        }, canAddStopAtTime);

        var canAddStopAfterDuration = StopTriggers
            .ToObservableChangeSet()
            .Select(_ => !StopTriggers.Any(t => t is StopAfterDurationTriggerViewModel))
            .ObserveOn(RxApp.MainThreadScheduler);

        _canAddStopAfterDurationTrigger = canAddStopAfterDuration.ToProperty(this, x => x.CanAddStopAfterDurationTrigger);

        AddStopAfterDurationTriggerCommand = ReactiveCommand.Create(() =>
        {
            var trigger = new StopAfterDurationTriggerViewModel();
            StopTriggers.Add(trigger);
            SelectedStopTrigger = trigger;
        }, canAddStopAfterDuration);

        RemoveStopTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t =>
        {
            StopTriggers.Remove(t);
            if (SelectedStopTrigger == t)
            {
                SelectedStopTrigger = null;
            }
        });

        EditStopTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t => SelectedStopTrigger = t);
        CloseStopTriggerSettingsCommand = ReactiveCommand.Create(() => SelectedStopTrigger = null);

        var canAddThenStartAnother = ThenTriggers
            .ToObservableChangeSet()
            .Select(_ => !ThenTriggers.Any(t => t is ThenStartAnotherTriggerViewModel))
            .ObserveOn(RxApp.MainThreadScheduler);

        _canAddThenStartAnotherTrigger = canAddThenStartAnother.ToProperty(this, x => x.CanAddThenStartAnotherTrigger);

        AddThenStartAnotherTriggerCommand = ReactiveCommand.Create(() =>
        {
            ThenTriggers.Clear();
            var trigger = new ThenStartAnotherTriggerViewModel(this);
            ThenTriggers.Add(trigger);
            SelectedThenTrigger = trigger;
        }, canAddThenStartAnother);

        RemoveThenTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t =>
        {
            ThenTriggers.Remove(t);
            if (SelectedThenTrigger == t)
            {
                SelectedThenTrigger = null;
            }
        });

        EditThenTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t => SelectedThenTrigger = t);
        CloseThenTriggerSettingsCommand = ReactiveCommand.Create(() => SelectedThenTrigger = null);

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
                AvailablePresetsForNext.Clear();
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

        AvailablePresetsForNext.Clear();
        foreach (var preset in presets)
        {
            if (preset.Id != _preset.Id)
            {
                AvailablePresetsForNext.Add(new NextPresetOption { PresetId = preset.Id, Name = preset.Name });
            }
        }

        foreach (var trigger in ThenTriggers.OfType<ThenStartAnotherTriggerViewModel>())
        {
            trigger.RefreshPresetVisibility();
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
        StopTriggers.Clear();
        ThenTriggers.Clear();

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

                var toRemoveStop = StopTriggers.ToList();
                foreach (var t in toRemoveStop)
                {
                    StopTriggers.Remove(t);
                }

                foreach (var s in presetSchedules.Where(s => s.Type == ScheduleType.Recurring))
                {
                    var trigger = new ScheduleTriggerViewModel
                    {
                        ExistingScheduleId = s.Id,
                        Use24HourFormat = s.Use24HourFormat,
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

                foreach (var s in presetSchedules.Where(s => s.Type == ScheduleType.StopRecurring))
                {
                    var trigger = new StopAtTimeTriggerViewModel
                    {
                        ExistingScheduleId = s.Id,
                        Use24HourFormat = s.Use24HourFormat,
                        Time = s.RecurringTime ?? TimeSpan.Zero,
                        RunOnMonday = s.DaysOfWeek.Contains(DayOfWeek.Monday),
                        RunOnTuesday = s.DaysOfWeek.Contains(DayOfWeek.Tuesday),
                        RunOnWednesday = s.DaysOfWeek.Contains(DayOfWeek.Wednesday),
                        RunOnThursday = s.DaysOfWeek.Contains(DayOfWeek.Thursday),
                        RunOnFriday = s.DaysOfWeek.Contains(DayOfWeek.Friday),
                        RunOnSaturday = s.DaysOfWeek.Contains(DayOfWeek.Saturday),
                        RunOnSunday = s.DaysOfWeek.Contains(DayOfWeek.Sunday)
                    };

                    if (ThenTriggers.Count == 0 && s.NextPresetId.HasValue)
                    {
                        var thenTrigger = new ThenStartAnotherTriggerViewModel(this)
                        {
                            NextPresetId = s.NextPresetId
                        };
                        var nextPreset = AvailablePresetsForNext.FirstOrDefault(p => p.PresetId == s.NextPresetId.Value);
                        if (nextPreset != null)
                        {
                            thenTrigger.SelectedNextPreset = nextPreset;
                            thenTrigger.NextPresetName = nextPreset.Name;
                        }
                        else
                        {
                            _ = LoadThenPresetNameAsync(thenTrigger, s.NextPresetId.Value);
                        }
                        ThenTriggers.Add(thenTrigger);
                    }

                    StopTriggers.Add(trigger);
                }

                foreach (var s in presetSchedules.Where(s => s.Type == ScheduleType.StopDuration))
                {
                    var trigger = new StopAfterDurationTriggerViewModel
                    {
                        ExistingScheduleId = s.Id,
                        Duration = s.AutoStopDuration ?? TimeSpan.FromHours(1),
                        DurationHours = s.AutoStopDuration?.Hours ?? 1,
                        DurationMinutes = s.AutoStopDuration?.Minutes ?? 0
                    };

                    if (ThenTriggers.Count == 0 && s.NextPresetId.HasValue)
                    {
                        var thenTrigger = new ThenStartAnotherTriggerViewModel(this)
                        {
                            NextPresetId = s.NextPresetId
                        };
                        var nextPreset = AvailablePresetsForNext.FirstOrDefault(p => p.PresetId == s.NextPresetId.Value);
                        if (nextPreset != null)
                        {
                            thenTrigger.SelectedNextPreset = nextPreset;
                            thenTrigger.NextPresetName = nextPreset.Name;
                        }
                        else
                        {
                            _ = LoadThenPresetNameAsync(thenTrigger, s.NextPresetId.Value);
                        }
                        ThenTriggers.Add(thenTrigger);
                    }

                    StopTriggers.Add(trigger);
                }
            });
        }
        catch
        {
            /* Ignore */
        }
    }

    private async Task LoadThenPresetNameAsync(ThenStartAnotherTriggerViewModel trigger, Guid presetId)
    {
        try
        {
            var preset = await _presetsApi.GetPresetAsync(presetId);
            if (preset != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var option = new NextPresetOption { PresetId = presetId, Name = preset.Name };
                    if (AvailablePresetsForNext.All(p => p.PresetId != presetId))
                    {
                        AvailablePresetsForNext.Add(option);
                    }
                    trigger.SelectedNextPreset = AvailablePresetsForNext.FirstOrDefault(p => p.PresetId == presetId);
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

        var thenTrigger = ThenTriggers.OfType<ThenStartAnotherTriggerViewModel>().FirstOrDefault();
        if (thenTrigger is { NextPresetId: null })
        {
            ErrorMessage = "Please select a session for 'Start Another Session' trigger.";
            return;
        }

        var stopDurationTrigger = StopTriggers.OfType<StopAfterDurationTriggerViewModel>().FirstOrDefault();
        if (stopDurationTrigger is { HasDurationError: true })
        {
            ErrorMessage = "Session Duration must be greater than 0.";
            return;
        }

        ErrorMessage = string.Empty;
        _preset.Modules =
        [
            .. ConfiguredModules.Select(vm =>
            {
                vm.SaveChangesToModel();
                return vm.Model;
            })
        ];
        _preset.Name = Name;

        try
        {
            var existingPreset = await _presetsApi.GetPresetAsync(_preset.Id);
            var isNew = existingPreset == null;
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
                    Use24HourFormat = trigger.Use24HourFormat
                };

                if (!trigger.ExistingScheduleId.HasValue && presetSchedules.Count > 0)
                {
                    var candidate = presetSchedules.FirstOrDefault(s => s.Type == ScheduleType.Recurring && !activeTriggerIds.Contains(s.Id));
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

            foreach (var stopTrigger in StopTriggers.OfType<StopAtTimeTriggerViewModel>())
            {
                var days = new List<DayOfWeek>();
                if (stopTrigger.RunOnMonday) days.Add(DayOfWeek.Monday);
                if (stopTrigger.RunOnTuesday) days.Add(DayOfWeek.Tuesday);
                if (stopTrigger.RunOnWednesday) days.Add(DayOfWeek.Wednesday);
                if (stopTrigger.RunOnThursday) days.Add(DayOfWeek.Thursday);
                if (stopTrigger.RunOnFriday) days.Add(DayOfWeek.Friday);
                if (stopTrigger.RunOnSaturday) days.Add(DayOfWeek.Saturday);
                if (stopTrigger.RunOnSunday) days.Add(DayOfWeek.Sunday);

                var thenStartTrigger = ThenTriggers.OfType<ThenStartAnotherTriggerViewModel>().FirstOrDefault();
                var nextPresetId = thenStartTrigger?.NextPresetId;

                var schedule = new SessionSchedule
                {
                    Id = stopTrigger.ExistingScheduleId ?? Guid.NewGuid(),
                    PresetId = _preset.Id,
                    Type = ScheduleType.StopRecurring,
                    Name = $"{Name} Stop Schedule",
                    IsEnabled = true,
                    RecurringTime = stopTrigger.Time,
                    DaysOfWeek = days,
                    NextPresetId = nextPresetId,
                    Use24HourFormat = stopTrigger.Use24HourFormat
                };

                if (!stopTrigger.ExistingScheduleId.HasValue && presetSchedules.Count > 0)
                {
                    var candidate = presetSchedules.FirstOrDefault(s => s.Type == ScheduleType.StopRecurring && !activeTriggerIds.Contains(s.Id));
                    if (candidate != null)
                    {
                        schedule.Id = candidate.Id;
                        stopTrigger.ExistingScheduleId = candidate.Id;
                    }
                }

                if (stopTrigger.ExistingScheduleId.HasValue)
                {
                    await _schedulerApi.UpdateScheduleAsync(schedule);
                }
                else
                {
                    await _schedulerApi.CreateScheduleAsync(schedule);
                }

                activeTriggerIds.Add(schedule.Id);
            }

            foreach (var durationTrigger in StopTriggers.OfType<StopAfterDurationTriggerViewModel>())
            {
                var thenStartTrigger = ThenTriggers.OfType<ThenStartAnotherTriggerViewModel>().FirstOrDefault();
                var nextPresetId = thenStartTrigger?.NextPresetId;

                var schedule = new SessionSchedule
                {
                    Id = durationTrigger.ExistingScheduleId ?? Guid.NewGuid(),
                    PresetId = _preset.Id,
                    Type = ScheduleType.StopDuration,
                    Name = $"{Name} Duration Stop",
                    IsEnabled = true,
                    AutoStopDuration = durationTrigger.Duration,
                    NextPresetId = nextPresetId
                };

                if (!durationTrigger.ExistingScheduleId.HasValue && presetSchedules.Count > 0)
                {
                    var candidate = presetSchedules.FirstOrDefault(s => s.Type == ScheduleType.StopDuration && !activeTriggerIds.Contains(s.Id));
                    if (candidate != null)
                    {
                        schedule.Id = candidate.Id;
                        durationTrigger.ExistingScheduleId = candidate.Id;
                    }
                }

                if (durationTrigger.ExistingScheduleId.HasValue)
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

            _toastService.Show("Preset saved successfully", NotificationType.Success);

            if (isNew)
            {
                _telemetry?.TrackEvent("PresetCreated", new Dictionary<string, object?>
                {
                    ["presetId"] = _preset.Id,
                    ["name"] = _preset.Name
                });
            }

            var presetSummaries = await _presetsApi.ListPresetsAsync();
            var fullPresets = new List<SessionPreset>();
            foreach (var summary in presetSummaries)
            {
                var fullPreset = await _presetsApi.GetPresetAsync(summary.Id);
                if (fullPreset != null)
                {
                    fullPresets.Add(fullPreset);
                }
            }

            var modules = await _modulesApi.ListModulesAsync();
            var moduleDefLookup = modules.ToDictionary(m => m.Id, m => m.Name);

            var presetData = fullPresets.Select(p => new Dictionary<string, object?>
            {
                ["id"] = p.Id.ToString(),
                ["name"] = TelemetryGuard.SafeString(p.Name),
                ["version"] = p.Version,
                ["moduleCount"] = p.Modules.Count,
                ["modules"] = p.Modules.Select(m =>
                {
                    var moduleName = moduleDefLookup.TryGetValue(m.ModuleId, out var name) ? name : "Unknown";
                    return new Dictionary<string, object?>
                    {
                        ["instanceId"] = m.InstanceId.ToString(),
                        ["moduleId"] = m.ModuleId.ToString(),
                        ["moduleName"] = TelemetryGuard.SafeString(moduleName),
                        ["customName"] = TelemetryGuard.SafeString(m.CustomName),
                        ["startDelayMs"] = (long)m.StartDelay.TotalMilliseconds,
                        ["settingsCount"] = m.Settings.Count,
                        ["settingKeys"] = m.Settings.Keys.Take(32).ToArray()
                    };
                }).ToArray()
            }).ToArray();

            _telemetry?.TrackEvent("PresetCount", new Dictionary<string, object?>
            {
                ["total"] = presetSummaries.Count,
                ["presets"] = presetData
            });

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _isFooterVisible.Dispose();
        _canAddAnyTrigger.Dispose();
        _canAddStopAtTimeTrigger.Dispose();
        _canAddStopAfterDurationTrigger.Dispose();
        _canAddThenStartAnotherTrigger.Dispose();
        _hasValidationErrors.Dispose();
        AddScheduleTriggerCommand.Dispose();
        AddStopAtTimeTriggerCommand.Dispose();
        AddStopAfterDurationTriggerCommand.Dispose();
        AddThenStartAnotherTriggerCommand.Dispose();

        foreach (var vm in ConfiguredModules)
        {
            vm.Dispose();
        }

        ConfiguredModules.Clear();

        GC.SuppressFinalize(this);
    }
}