using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class SessionPresetViewModel : ReactiveObject, IDisposable
{
    private readonly IDisposable? _validationSubscription;

    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public SessionPreset Model { get; }

    public bool HasValidationErrors
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsValid => !HasValidationErrors;

    public string? ValidationMessage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int ErrorCount
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<ConfiguredModuleViewModel> Modules { get; } = [];

    public SessionPresetViewModel(SessionPreset model, IReadOnlyList<ModuleDefinition> availableModules,
        IModulesApi modulesApi, IServiceProvider serviceProvider)
    {
        Model = model;

        var moduleVms = model.Modules
            .Select(m =>
            {
                var def = availableModules.FirstOrDefault(md => md.Id == m.ModuleId);
                return def != null ? new ConfiguredModuleViewModel(def, m, modulesApi, serviceProvider) : null;
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
