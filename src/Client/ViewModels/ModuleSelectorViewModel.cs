using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

// Wrapper for ModuleDefinition to handle UI state
public class ModuleDefinitionViewModel(ModuleDefinition definition) : ReactiveObject
{
    public ModuleDefinition Definition { get; } = definition;

    public bool IsJustAdded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}

public class ModuleSelectorViewModel : ReactiveObject
{
    private readonly IReadOnlyList<ModuleDefinition> _allModules;
    private readonly Action<ModuleDefinition> _onModuleSelected;

    public string SearchText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string SelectedCategory
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<string> Categories { get; } = [];

    public ObservableCollection<ModuleDefinitionViewModel> FilteredModules { get; } = [];

    public ICommand SelectModuleCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand CloseCommand { get; }

    public ModuleSelectorViewModel(
        IReadOnlyList<ModuleDefinition> allModules,
        Action<ModuleDefinition> onModuleSelected,
        Action onCancel)
    {
        _allModules = allModules;
        _onModuleSelected = onModuleSelected;

        SelectModuleCommand = ReactiveCommand.CreateFromTask<ModuleDefinitionViewModel>(SelectModule);
        SelectCategoryCommand = ReactiveCommand.Create<string>(cat => SelectedCategory = cat);
        CloseCommand = ReactiveCommand.Create(onCancel);

        InitializeCategories();

        SelectedCategory = "All";

        this.WhenAnyValue(x => x.SearchText, x => x.SelectedCategory)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => FilterModules());

        FilterModules();
    }

    private void InitializeCategories()
    {
        Categories.Add("All");
        var distinctCategories = _allModules
            .Select(m => m.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c);

        foreach (var cat in distinctCategories)
        {
            Categories.Add(cat);
        }
    }

    private void FilterModules()
    {
        var query = _allModules.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(m =>
                m.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
        {
            query = query.Where(m => string.Equals(m.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.OrderBy(m => m.Name).ToList();

        FilteredModules.Clear();
        foreach (var module in result)
        {
            FilteredModules.Add(new ModuleDefinitionViewModel(module));
        }
    }

    private async Task SelectModule(ModuleDefinitionViewModel vm)
    {
        _onModuleSelected(vm.Definition);

        if (vm.IsJustAdded)
        {
            return;
        }

        vm.IsJustAdded = true;
        await Task.Delay(1000);
        vm.IsJustAdded = false;
    }
}