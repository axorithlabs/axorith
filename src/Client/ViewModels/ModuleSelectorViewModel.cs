using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Axorith.Client.ViewModels;

public class ModuleDefinitionViewModel(ModuleDefinition definition) : ReactiveObject
{
    public ModuleDefinition Definition { get; } = definition;

    [Reactive]
    public bool IsJustAdded { get; set; }
}

public class CategoryViewModel : ReactiveObject
{
    public string Name { get; }

    [Reactive]
    public bool IsSelected { get; set; }

    public CategoryViewModel(string name, bool isSelected = false)
    {
        Name = name;
        IsSelected = isSelected;
    }
}

public class ModuleSelectorViewModel : ReactiveObject
{
    private readonly IReadOnlyList<ModuleDefinition> _allModules;
    private readonly Action<ModuleDefinition> _onModuleSelected;

    [Reactive]
    public string SearchText { get; set; } = string.Empty;

    [Reactive]
    public string SelectedCategory { get; set; } = "All";

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];

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

        SelectModuleCommand = ReactiveCommand.Create<ModuleDefinitionViewModel>(SelectModule);
        SelectCategoryCommand = ReactiveCommand.Create<CategoryViewModel>(SelectCategory);
        CloseCommand = ReactiveCommand.Create(onCancel);

        InitializeCategories();

        this.WhenAnyValue(x => x.SearchText, x => x.SelectedCategory)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => FilterModules());

        FilterModules();
    }

    private void InitializeCategories()
    {
        Categories.Add(new CategoryViewModel("All", true));

        var distinctCategories = _allModules
            .Select(m => m.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c);

        foreach (var cat in distinctCategories)
        {
            Categories.Add(new CategoryViewModel(cat));
        }
    }

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

    private async void SelectModule(ModuleDefinitionViewModel vm)
    {
        if (vm.IsJustAdded)
        {
            return;
        }

        _onModuleSelected(vm.Definition);
        vm.IsJustAdded = true;

        await Task.Delay(800);
        vm.IsJustAdded = false;
    }
}
