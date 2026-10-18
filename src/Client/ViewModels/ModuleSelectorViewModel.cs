using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Axorith.Client.ViewModels;

public partial class ModuleDefinitionViewModel(ModuleDefinition definition) : ReactiveObject
{
    public ModuleDefinition Definition { get; } = definition;

    [Reactive]
    public partial bool IsJustAdded { get; set; }
}

public partial class CategoryViewModel : ReactiveObject
{
    public string Name { get; }

    [Reactive]
    public partial bool IsSelected { get; set; }

    public CategoryViewModel(string name, bool isSelected = false)
    {
        Name = name;
        IsSelected = isSelected;
    }
}

public partial class ModuleSelectorViewModel : ReactiveObject
{
    private readonly IReadOnlyList<ModuleDefinition> _allModules;
    private readonly Action<ModuleDefinition> _onModuleSelected;

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string SelectedCategory { get; set; } = "All";

    public ObservableCollection<CategoryViewModel> Categories { get; }

    public ObservableCollection<ModuleDefinitionViewModel> FilteredModules { get; } = [];

    public ICommand CloseCommand { get; }

    public ModuleSelectorViewModel(
        IReadOnlyList<ModuleDefinition> allModules,
        Action<ModuleDefinition> onModuleSelected,
        Action onCancel)
    {
        _allModules = allModules;
        _onModuleSelected = onModuleSelected;
        Categories = new(_allModules
            .Select(module => module.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct()
            .OrderBy(category => category)
            .Select(category => new CategoryViewModel(category))
            .Prepend(new CategoryViewModel("All", true)));

        CloseCommand = ReactiveCommand.Create(onCancel);

        this.WhenAnyValue(x => x.SearchText, x => x.SelectedCategory)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => FilterModules());

        FilterModules();
    }

    [ReactiveCommand]
    private void SelectCategory(CategoryViewModel category)
    {
        foreach (var cat in Categories)
        {
            cat.IsSelected = cat == category;
        }

        SelectedCategory = category.Name;
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

    [ReactiveCommand]
    private async Task SelectModule(ModuleDefinitionViewModel vm)
    {
        if (vm.IsJustAdded) return;

        _onModuleSelected(vm.Definition);
        vm.IsJustAdded = true;

        await Task.Delay(800);
        vm.IsJustAdded = false;
    }
}
