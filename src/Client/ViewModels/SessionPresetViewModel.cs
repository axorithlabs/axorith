using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Axorith.Client.ViewModels;

public partial class SessionPresetViewModel : ReactiveObject, IDisposable
{
    private readonly IDisposable? _validationSubscription;
    private readonly IReadOnlyList<SessionSchedule> _schedules;

    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public SessionPreset Model { get; }
    public string CommitmentLabel => Model.FocusCommitment.Mode switch
    {
        FocusCommitmentMode.Strict => "Strict",
        FocusCommitmentMode.Locked => "Locked",
        _ => "Normal"
    };

    public string SessionSummary
    {
        get
        {
            var options = Model.FocusCommitment;
            return options.EndCondition switch
            {
                FocusEndCondition.Duration when options.Duration is { } value => $"Session duration: {value.TotalMinutes:0} min",
                FocusEndCondition.EndAt when options.EndAtLocalTime is { } endAt => $"Until {endAt:HH:mm}",
                _ => "Until stopped"
            };
        }
    }

    public string ModulesSummary
    {
        get
        {
            var names = Modules.Take(3).Select(module => module.DisplayName).ToArray();
            var remaining = Modules.Count - names.Length;
            return remaining > 0
                ? $"{string.Join(", ", names)} +{remaining}"
                : string.Join(", ", names);
        }
    }

    public bool HasSchedule => _schedules.Any(schedule => schedule.Type != ScheduleType.StopDuration);

    public string ScheduleSummary => string.Join(" | ", _schedules
        .Where(schedule => schedule.Type != ScheduleType.StopDuration)
        .Select(DescribeSchedule));

    private static string DescribeSchedule(SessionSchedule schedule) => schedule.Type switch
    {
        ScheduleType.OneTime when schedule.OneTimeDate is { } date =>
            $"Starts {date.ToLocalTime().ToString("ddd, d MMM yyyy", CultureInfo.CurrentCulture)} at " +
            FormatTime(TimeOnly.FromDateTime(date.ToLocalTime().DateTime), schedule.Use24HourFormat),
        ScheduleType.OneTime => "One-time start date not set",
        ScheduleType.Recurring => FormatRecurringSchedule(schedule, "Starts", "Recurring start time not set"),
        ScheduleType.StopRecurring => FormatRecurringSchedule(schedule, "Stops", "Recurring stop time not set"),
        _ => schedule.Name
    };

    private static string FormatRecurringSchedule(SessionSchedule schedule, string action, string missingTime)
    {
        if (schedule.RecurringTime is not { } time || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            return missingTime;
        }

        var days = schedule.DaysOfWeek.Distinct().OrderBy(day => ((int)day + 6) % 7).ToArray();
        var daysLabel = days.Length is 0 or 7
            ? "every day"
            : $"on {string.Join(", ", days.Select(day => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day)))}";

        return $"{action} {daysLabel} at {FormatTime(TimeOnly.FromTimeSpan(time), schedule.Use24HourFormat)}";
    }

    private static string FormatTime(TimeOnly time, bool use24HourFormat) =>
        time.ToString(use24HourFormat ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);

    [Reactive]
    public partial bool IsActive { get; set; }

    [Reactive]
    public partial bool HasValidationErrors { get; private set; }

    public bool IsValid => !HasValidationErrors;

    [Reactive]
    public partial string? ValidationMessage { get; private set; }

    [Reactive]
    public partial int ErrorCount { get; private set; }

    public ObservableCollection<ConfiguredModuleViewModel> Modules { get; } = [];

    public SessionPresetViewModel(SessionPreset model, IReadOnlyList<ModuleDefinition> availableModules,
        IModulesApi modulesApi, IServiceProvider serviceProvider, IReadOnlyList<SessionSchedule>? schedules = null)
    {
        Model = model;
        _schedules = schedules ?? [];

        var moduleVms = model.Modules
            .Select(m =>
            {
                var def = availableModules.FirstOrDefault(md => md.Id == m.ModuleId);
                return def != null ? new ConfiguredModuleViewModel(def, m, modulesApi, serviceProvider, model.Id) : null;
            })
            .Where(vm => vm != null);

        foreach (var vm in moduleVms)
        {
            Modules.Add(vm!);
        }

        _validationSubscription = Modules
            .Select(m => m.WhenAnyValue(x => x.HasErrors))
            .Merge()
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateValidationState());

        UpdateValidationState();
    }

    private void UpdateValidationState()
    {
        var modulesWithErrors = Modules.Where(m => m.HasErrors).ToList();
        ErrorCount = modulesWithErrors.Count;
        HasValidationErrors = ErrorCount > 0;

        if (ErrorCount == 0)
        {
            ValidationMessage = null;
        }
        else
        {
            var errorModuleNames = string.Join(", ", modulesWithErrors.Select(m => m.DisplayName));
            ValidationMessage = ErrorCount == 1
                ? $"{errorModuleNames} requires configuration"
                : $"{ErrorCount} modules require configuration: {errorModuleNames}";
        }

        this.RaisePropertyChanged(nameof(IsValid));
    }

    public void Dispose()
    {
        _validationSubscription?.Dispose();

        foreach (var vm in Modules)
        {
            vm.Dispose();
        }

        Modules.Clear();
    }
}
