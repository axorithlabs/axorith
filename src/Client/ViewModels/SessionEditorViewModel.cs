using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using DynamicData;
using DynamicData.Binding;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using PresetSummary = Axorith.Client.CoreSdk.Abstractions.PresetSummary;

namespace Axorith.Client.ViewModels;

public abstract class TriggerViewModel : ReactiveObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract string IconKey { get; }
    public virtual bool HasError => false;
}

public abstract class ScheduledTriggerViewModel : TriggerViewModel
{
    public Guid? ExistingScheduleId { get; set; }
}

public abstract partial class TimeTriggerViewModel(TimeSpan defaultTime) : ScheduledTriggerViewModel
{
    private TimeSpan _time = defaultTime;
    private decimal? _hours = defaultTime.Hours;
    private decimal? _minutes = defaultTime.Minutes;
    private bool _isAm = defaultTime.Hours < 12;
    private bool _use24HourFormat = true;
    private bool _isUpdatingTime;

    public override string IconKey => "TimerIcon";
    public abstract string EditorTitle { get; }
    public abstract string TimeLabel { get; }
    public abstract IBrush EditorAccentBrush { get; }

    public override string Description
    {
        get
        {
            var days = GetSelectedDays();
            var daysText = days.Count == 7
                ? "Every day"
                : string.Join(", ", days.Select(day => day.ToString()[..3]));
            var timeText = Use24HourFormat ? $"{Time:hh\\:mm}" : TimeOnly.FromTimeSpan(Time).ToString("h:mm tt", CultureInfo.CurrentCulture);
            return $"{timeText} • {daysText}";
        }
    }

    public TimeSpan Time
    {
        get => _time;
        set
        {
            this.RaiseAndSetIfChanged(ref _time, value);
            UpdateTimeInputs();
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    public decimal? Hours { get => _hours; set => SetTimePart(ref _hours, value); }

    public decimal? Minutes { get => _minutes; set => SetTimePart(ref _minutes, value); }

    public bool HasTimeError => !_hours.HasValue || !_minutes.HasValue;

    public bool IsAm
    {
        get => _isAm;
        set
        {
            if (_isAm == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isAm, value);
            UpdateTimeFromInputs();
        }
    }

    public bool Use24HourFormat
    {
        get => _use24HourFormat;
        set
        {
            if (_use24HourFormat == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _use24HourFormat, value);
            UpdateTimeInputs();
            this.RaisePropertyChanged(nameof(Description));
        }
    }

    [Reactive(nameof(Description))]
    public partial bool RunOnMonday { get; set; } = true;

    [Reactive(nameof(Description))]
    public partial bool RunOnTuesday { get; set; } = true;

    [Reactive(nameof(Description))]
    public partial bool RunOnWednesday { get; set; } = true;

    [Reactive(nameof(Description))]
    public partial bool RunOnThursday { get; set; } = true;

    [Reactive(nameof(Description))]
    public partial bool RunOnFriday { get; set; } = true;

    [Reactive(nameof(Description))]
    public partial bool RunOnSaturday { get; set; }

    [Reactive(nameof(Description))]
    public partial bool RunOnSunday { get; set; }

    public void LoadSchedule(SessionSchedule schedule)
    {
        ExistingScheduleId = schedule.Id;
        Use24HourFormat = schedule.Use24HourFormat;
        Time = schedule.RecurringTime ?? TimeSpan.Zero;
        var days = schedule.DaysOfWeek;
        RunOnMonday = days.Contains(DayOfWeek.Monday);
        RunOnTuesday = days.Contains(DayOfWeek.Tuesday);
        RunOnWednesday = days.Contains(DayOfWeek.Wednesday);
        RunOnThursday = days.Contains(DayOfWeek.Thursday);
        RunOnFriday = days.Contains(DayOfWeek.Friday);
        RunOnSaturday = days.Contains(DayOfWeek.Saturday);
        RunOnSunday = days.Contains(DayOfWeek.Sunday);
    }

    public List<DayOfWeek> GetSelectedDays()
    {
        var days = new List<DayOfWeek>(7);
        if (RunOnMonday) days.Add(DayOfWeek.Monday);
        if (RunOnTuesday) days.Add(DayOfWeek.Tuesday);
        if (RunOnWednesday) days.Add(DayOfWeek.Wednesday);
        if (RunOnThursday) days.Add(DayOfWeek.Thursday);
        if (RunOnFriday) days.Add(DayOfWeek.Friday);
        if (RunOnSaturday) days.Add(DayOfWeek.Saturday);
        if (RunOnSunday) days.Add(DayOfWeek.Sunday);
        return days;
    }

    private void SetTimePart(ref decimal? field, decimal? value)
    {
        if (field == value)
            return;

        this.RaiseAndSetIfChanged(ref field, value);
        this.RaisePropertyChanged(nameof(HasTimeError));
        if (value.HasValue)
            UpdateTimeFromInputs();
    }

    private void UpdateTimeInputs()
    {
        if (_isUpdatingTime)
        {
            return;
        }

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
        if (_isUpdatingTime || !_hours.HasValue || !_minutes.HasValue)
        {
            return;
        }

        _isUpdatingTime = true;
        try
        {
            var hours = (int)_hours.Value;
            if (!Use24HourFormat)
            {
                hours %= 12;
                if (!_isAm)
                {
                    hours += 12;
                }
            }

            Time = new TimeSpan(hours, (int)_minutes.Value, 0);
        }
        finally
        {
            _isUpdatingTime = false;
        }
    }
}

public sealed class ScheduleTriggerViewModel() : TimeTriggerViewModel(new TimeSpan(9, 0, 0))
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#00AAFF"));

    public override string Title => "Time Schedule";
    public override string EditorTitle => "Edit Schedule";
    public override string TimeLabel => "Start Time";
    public override IBrush EditorAccentBrush => Accent;
}

public sealed record NextPresetOption(Guid? PresetId, string Name);

public sealed class StopAtTimeTriggerViewModel() : TimeTriggerViewModel(new TimeSpan(17, 0, 0))
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#FF6B6B"));

    public override string Title => "Fixed Time";
    public override string EditorTitle => "Edit Fixed Time Stop";
    public override string TimeLabel => "Stop Time";
    public override IBrush EditorAccentBrush => Accent;
}

public class StopAfterDurationTriggerViewModel : ScheduledTriggerViewModel
{
    public override string Title => "Session Duration";
    public override string IconKey => "TimerIcon";

    public override string Description => DurationHours > 0
        ? $"After {DurationHours}h {DurationMinutes}m"
        : $"After {DurationMinutes}m";

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
    public decimal? DurationHours { get => _durationHours; set => SetDurationPart(ref _durationHours, value); }

    private decimal? _durationMinutes = 0;
    public decimal? DurationMinutes { get => _durationMinutes; set => SetDurationPart(ref _durationMinutes, value); }

    public bool HasDurationError => !_durationHours.HasValue || !_durationMinutes.HasValue ||
                                    ((int)(_durationHours ?? 0) == 0 && (int)(_durationMinutes ?? 0) == 0);

    public override bool HasError => HasDurationError;

    private void SetDurationPart(ref decimal? field, decimal? value)
    {
        this.RaiseAndSetIfChanged(ref field, value);
        this.RaisePropertyChanged(nameof(HasDurationError));
        this.RaisePropertyChanged(nameof(HasError));
        this.RaisePropertyChanged(nameof(Description));
        if (_durationHours.HasValue && _durationMinutes.HasValue)
        {
            Duration = TimeSpan.FromHours((int)_durationHours.Value) + TimeSpan.FromMinutes((int)_durationMinutes.Value);
        }
    }
}

public partial class ThenActionTriggerViewModel(SessionEditorViewModel parent, AfterEndBehavior behavior) : TriggerViewModel
{
    public AfterEndBehavior Behavior { get; } = behavior;

    public override string Title => Behavior switch
    {
        AfterEndBehavior.StartNextWorkspace => "Start next Workspace",
        AfterEndBehavior.LockPc => "Lock PC",
        AfterEndBehavior.Sleep => "Sleep",
        AfterEndBehavior.SignOut => "Sign out",
        AfterEndBehavior.ShutDownPc => "Shut down PC",
        _ => string.Empty
    };

    public override string IconKey => "PlayIcon";

    public override string Description
    {
        get
        {
            return Behavior == AfterEndBehavior.StartNextWorkspace
                ? NextPresetId.HasValue && !string.IsNullOrWhiteSpace(NextPresetName)
                    ? $"Start '{NextPresetName}'"
                    : "Choose a Workspace"
                : Behavior switch
                {
                    AfterEndBehavior.LockPc => "Lock the Windows user",
                    AfterEndBehavior.Sleep => "Put the PC to sleep",
                    AfterEndBehavior.SignOut => "Sign out of Windows",
                    AfterEndBehavior.ShutDownPc => "Turn off the PC",
                    _ => string.Empty
                };
        }
    }

    [Reactive(nameof(Description), nameof(HasError))]
    public partial Guid? NextPresetId { get; set; }

    [Reactive(nameof(Description))]
    public partial string? NextPresetName { get; set; }

    public bool IsNextPresetSelectionVisible => Behavior == AfterEndBehavior.StartNextWorkspace &&
                                                parent.AvailablePresetsForNext.Count > 0;

    public bool IsNoOtherPresetsAvailable => Behavior == AfterEndBehavior.StartNextWorkspace &&
                                             parent.AvailablePresetsForNext.Count == 0;

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

    public override bool HasError => Behavior == AfterEndBehavior.StartNextWorkspace && !NextPresetId.HasValue;

    public void RefreshPresetVisibility()
    {
        this.RaisePropertyChanged(nameof(IsNextPresetSelectionVisible));
        this.RaisePropertyChanged(nameof(IsNoOtherPresetsAvailable));
        this.RaisePropertyChanged(nameof(HasError));
    }
}

public partial class SessionEditorViewModel : ReactiveObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IModulesApi _modulesApi;
    private readonly IPresetsApi _presetsApi;
    private readonly ISchedulerApi _schedulerApi;
    private readonly IToastNotificationService _toastService;
    private readonly IServiceProvider _serviceProvider;

    private IReadOnlyList<ModuleDefinition> _availableModules = [];
    private SessionPreset _preset = new(id: Guid.NewGuid());
    private FocusCommitmentOptions _focusCommitment = new();
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
            ErrorMessage = !string.IsNullOrWhiteSpace(value) ? string.Empty : "Preset name cannot be empty.";
        }
    } = string.Empty;

    [Reactive]
    public partial string? ErrorMessage { get; private set; }

    [Reactive]
    public partial string? FocusCommitmentError { get; private set; }

    public IReadOnlyList<string> FocusCommitmentModes { get; } = ["Normal", "Locked", "Strict"];
    public IReadOnlyList<string> BreakOptions { get; } = ["No breaks", "One 5-minute break", "Custom break budget"];
    public IReadOnlyList<string> ScheduleLockOptions { get; } = ["Off", "5 minutes", "15 minutes", "1 hour"];

    public int FocusCommitmentModeIndex
    {
        get => (int)_focusCommitment.Mode;
        set
        {
            if (value is < 0 or > 2) return;
            _focusCommitment.Mode = (FocusCommitmentMode)value;
            RaiseFocusCommitmentChanged();
            ValidateFocusCommitment();
        }
    }

    public bool IsCommittedMode => _focusCommitment.Mode != FocusCommitmentMode.Normal;
    public bool IsStrictMode => _focusCommitment.Mode == FocusCommitmentMode.Strict;
    public bool IsCustomBreakBudget => BreakPresetIndex == 2;
    private bool _customBreakBudgetSelected;

    public int BreakPresetIndex
    {
        get
        {
            var options = _focusCommitment;
            return _customBreakBudgetSelected ? 2 : options.BreakCount == 0 ? 0 :
                options.BreakCount == 1 && options.BreakDuration == TimeSpan.FromMinutes(5) ? 1 : 2;
        }
        set
        {
            _customBreakBudgetSelected = value == 2;
            if (value == 0)
            {
                _focusCommitment.BreakCount = 0;
            }
            else if (value == 1)
            {
                _focusCommitment.BreakCount = 1;
                _focusCommitment.BreakDuration = TimeSpan.FromMinutes(5);
            }
            else if (_focusCommitment.BreakCount == 0 || BreakPresetIndex != 2)
            {
                _focusCommitment.BreakCount = 1;
                _focusCommitment.BreakDuration = TimeSpan.FromMinutes(5);
            }
            RaiseFocusCommitmentChanged();
            ValidateFocusCommitment();
        }
    }

    public int BreakCount
    {
        get => _focusCommitment.BreakCount;
        set
        {
            _focusCommitment.BreakCount = Math.Clamp(value, 1, 5);
            ValidateFocusCommitment();
        }
    }

    public int BreakDurationMinutes
    {
        get => (int)Math.Clamp(Math.Round(_focusCommitment.BreakDuration.TotalMinutes), 1, 60);
        set
        {
            _focusCommitment.BreakDuration = TimeSpan.FromMinutes(Math.Clamp(value, 1, 60));
            ValidateFocusCommitment();
        }
    }

    public int ScheduleLockIndex
    {
        get => _focusCommitment.ScheduleLockMinutes switch { 5 => 1, 15 => 2, 60 => 3, _ => 0 };
        set
        {
            _focusCommitment.ScheduleLockMinutes = value switch { 1 => 5, 2 => 15, 3 => 60, _ => 0 };
            ValidateFocusCommitment();
        }
    }

    [Reactive]
    public partial string ScheduledConfigurationLockStatus { get; private set; } = "Configuration unlocked";

    public ObservableCollection<TriggerViewModel> Triggers { get; } = [];
    public bool HasScheduledStart => Triggers.Any(trigger => trigger is ScheduleTriggerViewModel);
    public ObservableCollection<TriggerViewModel> StopTriggers { get; } = [];
    public ObservableCollection<TriggerViewModel> ThenTriggers { get; } = [];
    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = [];

    [Reactive]
    public partial ConfiguredModuleViewModel? SelectedModule { get; set; }

    [Reactive]
    public partial TriggerViewModel? SelectedTrigger { get; set; }

    [Reactive]
    public partial TriggerViewModel? SelectedStopTrigger { get; set; }

    [Reactive]
    public partial ModuleSelectorViewModel? ModuleSelector { get; set; }

    public ICommand SaveAndCloseCommand { get; }
    public ICommand RemoveModuleCommand { get; }
    public ICommand OpenModuleSettingsCommand { get; }
    public ICommand CloseModuleSettingsCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand OpenAddModuleCommand { get; }

    public ReactiveCommand<Unit, Unit> AddScheduleTriggerCommand { get; }
    public ICommand RemoveTriggerCommand { get; }
    public ICommand EditTriggerCommand { get; }

    public ReactiveCommand<Unit, Unit> AddStopAtTimeTriggerCommand { get; }
    public ReactiveCommand<Unit, Unit> AddStopAfterDurationTriggerCommand { get; }
    public ICommand RemoveStopTriggerCommand { get; }
    public ICommand EditStopTriggerCommand { get; }

    private readonly ObservableAsPropertyHelper<bool> _canAddStopAtTimeTrigger;
    public bool CanAddStopAtTimeTrigger => _canAddStopAtTimeTrigger.Value;

    private readonly ObservableAsPropertyHelper<bool> _canAddStopAfterDurationTrigger;
    public bool CanAddStopAfterDurationTrigger => _canAddStopAfterDurationTrigger.Value;

    public ReactiveCommand<AfterEndBehavior, Unit> AddThenActionTriggerCommand { get; }
    public ICommand RemoveThenTriggerCommand { get; }

    private readonly ObservableAsPropertyHelper<bool> _canAddThenActionTrigger;
    public bool CanAddThenActionTrigger => _canAddThenActionTrigger.Value;

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
        Triggers.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasScheduledStart));
        _shell = shell;
        _modulesApi = modulesApi;
        _presetsApi = presetsApi;
        _schedulerApi = schedulerApi;
        _toastService = toastService;
        _serviceProvider = serviceProvider;

        _isFooterVisible = this.WhenAnyValue(x => x.SelectedModule, x => x.ModuleSelector, x => x.SelectedTrigger,
                x => x.SelectedStopTrigger)
            .Select(t => t.Item1 == null && t.Item2 == null && t.Item3 == null && t.Item4 == null)
            .ToProperty(this, x => x.IsFooterVisible);

        var moduleValidationErrors = ConfiguredModules
            .ToObservableChangeSet()
            .AutoRefresh(m => m.HasErrors)
            .ToCollection()
            .Select(modules => modules.Any(m => m.HasErrors));

        var focusCommitmentValidationErrors = this.WhenAnyValue(vm => vm.FocusCommitmentError)
            .Select(error => !string.IsNullOrWhiteSpace(error));

        _hasValidationErrors = moduleValidationErrors
            .CombineLatest(focusCommitmentValidationErrors,
                (hasModuleErrors, hasFocusCommitmentErrors) => hasModuleErrors || hasFocusCommitmentErrors)
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

        OpenAddModuleCommand = ReactiveCommand.Create(() =>
            ModuleSelector = new ModuleSelectorViewModel(_availableModules, OnModuleAdded, () => ModuleSelector = null));

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

        OpenModuleSettingsCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(moduleVm => SelectedModule = moduleVm);
        CloseModuleSettingsCommand = ReactiveCommand.Create(() => SelectedModule = null);

        MoveUpCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(vm => MoveModule(vm, -1));
        MoveDownCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(vm => MoveModule(vm, 1));

        var canAddSchedule = CanAddTrigger<ScheduleTriggerViewModel>(Triggers);

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

        var canAddStopAtTime = CanAddTrigger<StopAtTimeTriggerViewModel>(StopTriggers);

        _canAddStopAtTimeTrigger = canAddStopAtTime.ToProperty(this, x => x.CanAddStopAtTimeTrigger);

        AddStopAtTimeTriggerCommand = ReactiveCommand.Create(() => AddStopTrigger(new StopAtTimeTriggerViewModel()),
            canAddStopAtTime);

        var canAddStopAfterDuration = CanAddTrigger<StopAfterDurationTriggerViewModel>(StopTriggers);

        _canAddStopAfterDurationTrigger =
            canAddStopAfterDuration.ToProperty(this, x => x.CanAddStopAfterDurationTrigger);

        AddStopAfterDurationTriggerCommand = ReactiveCommand.Create(
            () => AddStopTrigger(new StopAfterDurationTriggerViewModel()), canAddStopAfterDuration);

        RemoveStopTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t =>
        {
            StopTriggers.Remove(t);
            ValidateFocusCommitment();
            if (SelectedStopTrigger == t)
            {
                SelectedStopTrigger = null;
            }
        });

        EditStopTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t => SelectedStopTrigger = t);

        var canAddThenAction = ThenTriggers
            .ToObservableChangeSet()
            .Select(_ => ThenTriggers.Count == 0)
            .ObserveOn(RxApp.MainThreadScheduler);

        _canAddThenActionTrigger = canAddThenAction.ToProperty(this, x => x.CanAddThenActionTrigger);

        AddThenActionTriggerCommand = ReactiveCommand.Create<AfterEndBehavior>(behavior =>
        {
            if (behavior is AfterEndBehavior.DoNothing || !Enum.IsDefined(behavior))
            {
                return;
            }

            AddThenTrigger(behavior);
        }, canAddThenAction);

        RemoveThenTriggerCommand = ReactiveCommand.Create<TriggerViewModel>(t => ThenTriggers.Remove(t));

        InitializationTask = InitializeAsync();
    }

    private static IObservable<bool> CanAddTrigger<T>(ObservableCollection<TriggerViewModel> triggers) => triggers
        .ToObservableChangeSet()
        .Select(_ => !triggers.Any(trigger => trigger is T))
        .ObserveOn(RxApp.MainThreadScheduler);

    private void MoveModule(ConfiguredModuleViewModel module, int offset)
    {
        var index = ConfiguredModules.IndexOf(module);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= ConfiguredModules.Count) return;
        ConfiguredModules.Move(index, target);
        UpdateModuleLinks();
    }

    private void AddStopTrigger(ScheduledTriggerViewModel trigger)
    {
        StopTriggers.Add(trigger);
        ValidateFocusCommitment();
        SelectedStopTrigger = trigger;
    }

    [ReactiveCommand]
    private void CloseTrigger(TriggerViewModel trigger)
    {
        if (ReferenceEquals(SelectedTrigger, trigger))
            SelectedTrigger = null;
        if (ReferenceEquals(SelectedStopTrigger, trigger))
            SelectedStopTrigger = null;
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
                AvailablePresetsForNext.Add(new NextPresetOption(preset.Id, preset.Name));
            }
        }

        foreach (var trigger in ThenTriggers.OfType<ThenActionTriggerViewModel>())
        {
            trigger.RefreshPresetVisibility();
        }

    }

    private void LoadFromPreset()
    {
        _focusCommitment = new FocusCommitmentOptions(_preset.FocusCommitment ?? new FocusCommitmentOptions());
        FocusCommitmentError = null;
        _customBreakBudgetSelected = _focusCommitment.BreakCount > 0 &&
                                     (_focusCommitment.BreakCount != 1 ||
                                      _focusCommitment.BreakDuration != TimeSpan.FromMinutes(5));
        Name = _preset.Name;
        RaiseFocusCommitmentChanged();
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
            var lockStatus = await _schedulerApi.GetConfigurationLockStatusAsync(_preset.Id);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ScheduledConfigurationLockStatus = lockStatus.IsLocked
                    ? lockStatus.StartsIn is { } startsIn && startsIn > TimeSpan.Zero
                        ? $"Configuration locked · Session starts in {(int)Math.Ceiling(startsIn.TotalMinutes)} min"
                        : "Configuration locked · Session starts now"
                    : "Configuration unlocked";
                if (Enum.IsDefined(_focusCommitment.AfterEnd) &&
                    _focusCommitment.AfterEnd != AfterEndBehavior.DoNothing &&
                    (_focusCommitment.AfterEnd != AfterEndBehavior.StartNextWorkspace ||
                     _focusCommitment.NextWorkspaceId.HasValue))
                {
                    AddThenTrigger(_focusCommitment.AfterEnd, _focusCommitment.NextWorkspaceId);
                }

                var toRemove = Triggers.Where(t => t is ScheduleTriggerViewModel).ToList();
                foreach (var t in toRemove)
                {
                    Triggers.Remove(t);
                }

                StopTriggers.Clear();

                foreach (var schedule in presetSchedules)
                {
                    ScheduledTriggerViewModel? trigger = schedule.Type switch
                    {
                        ScheduleType.Recurring => new ScheduleTriggerViewModel(),
                        ScheduleType.StopRecurring => new StopAtTimeTriggerViewModel(),
                        ScheduleType.StopDuration => new StopAfterDurationTriggerViewModel
                        {
                            ExistingScheduleId = schedule.Id,
                            Duration = schedule.AutoStopDuration ?? TimeSpan.FromHours(1),
                            DurationHours = schedule.AutoStopDuration?.Hours ?? 1,
                            DurationMinutes = schedule.AutoStopDuration?.Minutes ?? 0
                        },
                        _ => null
                    };
                    if (trigger is null) continue;
                    if (trigger is TimeTriggerViewModel time) time.LoadSchedule(schedule);
                    if (trigger is ScheduleTriggerViewModel)
                    {
                        Triggers.Add(trigger);
                        continue;
                    }

                    if (ThenTriggers.Count == 0 && schedule.NextPresetId is { } nextPresetId)
                        AddThenTrigger(AfterEndBehavior.StartNextWorkspace, nextPresetId);
                    StopTriggers.Add(trigger);
                }

                ValidateFocusCommitment();
            });
        }
        catch
        {
            /* Ignore */
        }
    }

    private void AddThenTrigger(AfterEndBehavior behavior, Guid? nextPresetId = null)
    {
        var trigger = new ThenActionTriggerViewModel(this, behavior)
        {
            NextPresetId = behavior == AfterEndBehavior.StartNextWorkspace ? nextPresetId : null
        };

        if (trigger.NextPresetId is { } id)
        {
            if (AvailablePresetsForNext.FirstOrDefault(p => p.PresetId == id) is { } preset)
            {
                trigger.SelectedNextPreset = preset;
            }
            else
            {
                _ = LoadThenPresetNameAsync(trigger, id);
            }
        }

        ThenTriggers.Add(trigger);
    }

    private async Task LoadThenPresetNameAsync(ThenActionTriggerViewModel trigger, Guid presetId)
    {
        try
        {
            var preset = await _presetsApi.GetPresetAsync(presetId);
            if (preset != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var option = new NextPresetOption(presetId, preset.Name);
                    if (AvailablePresetsForNext.All(p => p.PresetId != presetId))
                    {
                        AvailablePresetsForNext.Add(option);
                    }

                    trigger.SelectedNextPreset = AvailablePresetsForNext.FirstOrDefault(p => p.PresetId == presetId);
                });
            }
        }
        catch
        {
            /* Ignore */
        }
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

        var thenTrigger = ThenTriggers.OfType<ThenActionTriggerViewModel>().FirstOrDefault();
        if (thenTrigger is { Behavior: AfterEndBehavior.StartNextWorkspace, NextPresetId: null })
        {
            ErrorMessage = "Please select a Workspace for the Start next Workspace action.";
            return;
        }

        if (thenTrigger is { Behavior: not AfterEndBehavior.DoNothing } && StopTriggers.Count == 0)
        {
            ErrorMessage = "Add a Stop Trigger before configuring an after-end action.";
            return;
        }

        var stopDurationTrigger = StopTriggers.OfType<StopAfterDurationTriggerViewModel>().FirstOrDefault();
        if (stopDurationTrigger is { HasDurationError: true })
        {
            ErrorMessage = "Session Duration must be greater than 0.";
            return;
        }

        if (!ValidateFocusCommitment(applyOptions: true))
        {
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
        _preset.FocusCommitment = new FocusCommitmentOptions(_focusCommitment);

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

            var thenStartTrigger = ThenTriggers.OfType<ThenActionTriggerViewModel>().FirstOrDefault();
            var nextPresetId = thenStartTrigger is { Behavior: AfterEndBehavior.StartNextWorkspace }
                ? thenStartTrigger.NextPresetId
                : null;

            foreach (var trigger in Triggers.Concat(StopTriggers).OfType<ScheduledTriggerViewModel>())
            {
                var schedule = trigger switch
                {
                    ScheduleTriggerViewModel time => new SessionSchedule
                    {
                        Type = ScheduleType.Recurring,
                        Name = $"{Name} Schedule",
                        RecurringTime = time.Time,
                        DaysOfWeek = time.GetSelectedDays(),
                        Use24HourFormat = time.Use24HourFormat
                    },
                    StopAtTimeTriggerViewModel time => new SessionSchedule
                    {
                        Type = ScheduleType.StopRecurring,
                        Name = $"{Name} Stop Schedule",
                        RecurringTime = time.Time,
                        DaysOfWeek = time.GetSelectedDays(),
                        NextPresetId = nextPresetId,
                        Use24HourFormat = time.Use24HourFormat
                    },
                    StopAfterDurationTriggerViewModel duration => new SessionSchedule
                    {
                        Type = ScheduleType.StopDuration,
                        Name = $"{Name} Duration Stop",
                        AutoStopDuration = duration.Duration,
                        NextPresetId = nextPresetId
                    },
                    _ => throw new InvalidOperationException($"Unsupported trigger type: {trigger.GetType().Name}")
                };
                schedule.Id = trigger.ExistingScheduleId ?? Guid.NewGuid();
                schedule.PresetId = _preset.Id;
                schedule.IsEnabled = true;
                await SaveScheduleAsync(trigger, schedule, presetSchedules, activeTriggerIds);
            }

            foreach (var s in presetSchedules.Where(s => !activeTriggerIds.Contains(s.Id)))
            {
                await _schedulerApi.DeleteScheduleAsync(s.Id);
            }

            _toastService.Show("Preset saved successfully", NotificationType.Success, "Presets");

            Cancel();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            ErrorMessage = "Name already taken. Please choose a different name.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save: {ex.Message}";
        }
    }

    private async Task SaveScheduleAsync(
        ScheduledTriggerViewModel trigger,
        SessionSchedule schedule,
        IReadOnlyList<SessionSchedule> existingSchedules,
        HashSet<Guid> activeTriggerIds)
    {
        if (!trigger.ExistingScheduleId.HasValue)
        {
            var candidate = existingSchedules.FirstOrDefault(existing =>
                existing.Type == schedule.Type && !activeTriggerIds.Contains(existing.Id));
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

    private bool ValidateFocusCommitment(bool applyOptions = false)
    {
        var options = _focusCommitment;
        if (options.BreakCount is < 0 or > 5 || options.BreakCount > 0 &&
            (options.BreakDuration < TimeSpan.FromMinutes(1) || options.BreakDuration > TimeSpan.FromMinutes(60)))
        {
            FocusCommitmentError = "Break budget must be between 0 and 5 breaks of 1 to 60 minutes each.";
            return false;
        }

        if (options.ScheduleLockMinutes is not (0 or 5 or 15 or 60))
        {
            FocusCommitmentError = "Choose Off, 5 minutes, 15 minutes, or 1 hour for the scheduled configuration lock.";
            return false;
        }

        if (options.Mode != FocusCommitmentMode.Normal)
        {
            if (StopTriggers.Count == 0)
            {
                FocusCommitmentError = "Add one Stop Trigger to set when a Locked or Strict session ends.";
                return false;
            }

            if (StopTriggers.Count > 1)
            {
                FocusCommitmentError = "Use only one Stop Trigger for a Locked or Strict session.";
                return false;
            }

            switch (StopTriggers[0])
            {
                case StopAfterDurationTriggerViewModel duration:
                    if (applyOptions)
                    {
                        options.EndCondition = FocusEndCondition.Duration;
                        options.Duration = duration.Duration;
                        options.EndAtLocalTime = null;
                        options.EndAtDaysOfWeek = [];
                    }
                    break;
                case StopAtTimeTriggerViewModel fixedTime:
                    if (applyOptions)
                    {
                        options.EndCondition = FocusEndCondition.EndAt;
                        options.Duration = null;
                        options.EndAtLocalTime = TimeOnly.FromTimeSpan(fixedTime.Time);
                        options.EndAtDaysOfWeek = fixedTime.GetSelectedDays();
                    }
                    break;
                default:
                    FocusCommitmentError = "Choose a Fixed Time or Session Duration Stop Trigger.";
                    return false;
            }

        }
        else
        {
            if (applyOptions)
            {
                options.EndCondition = FocusEndCondition.None;
                options.Duration = null;
                options.EndAtLocalTime = null;
                options.EndAtDaysOfWeek = [];
            }
        }

        if (applyOptions)
        {
            var thenTrigger = ThenTriggers.OfType<ThenActionTriggerViewModel>().FirstOrDefault();
            options.AfterEnd = thenTrigger?.Behavior ?? AfterEndBehavior.DoNothing;
            options.NextWorkspaceId = options.AfterEnd == AfterEndBehavior.StartNextWorkspace
                ? thenTrigger?.NextPresetId
                : null;
            if (options.AfterEnd == AfterEndBehavior.StartNextWorkspace && !options.NextWorkspaceId.HasValue)
            {
                FocusCommitmentError = "Select the Workspace for the Start next Workspace action.";
                return false;
            }
        }

        FocusCommitmentError = null;
        return true;
    }

    private void RaiseFocusCommitmentChanged()
    {
        this.RaisePropertyChanged(nameof(FocusCommitmentModeIndex));
        this.RaisePropertyChanged(nameof(IsCommittedMode));
        this.RaisePropertyChanged(nameof(IsStrictMode));
        this.RaisePropertyChanged(nameof(BreakPresetIndex));
        this.RaisePropertyChanged(nameof(IsCustomBreakBudget));
        this.RaisePropertyChanged(nameof(BreakCount));
        this.RaisePropertyChanged(nameof(BreakDurationMinutes));
        this.RaisePropertyChanged(nameof(ScheduleLockIndex));
    }

    [ReactiveCommand]
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
        _canAddThenActionTrigger.Dispose();
        _hasValidationErrors.Dispose();
        AddScheduleTriggerCommand.Dispose();
        AddStopAtTimeTriggerCommand.Dispose();
        AddStopAfterDurationTriggerCommand.Dispose();
        AddThenActionTriggerCommand.Dispose();

        foreach (var vm in ConfiguredModules)
        {
            vm.Dispose();
        }

        ConfiguredModules.Clear();

        GC.SuppressFinalize(this);
    }
}
