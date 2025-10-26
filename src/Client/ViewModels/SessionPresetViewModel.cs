using System.Collections.ObjectModel;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     A ViewModel wrapper for a SessionPreset model, responsible for preparing data for the View.
/// </summary>
public class SessionPresetViewModel : ReactiveObject
{
    private readonly SessionPreset _model;

    public Guid Id => _model.Id;
    public string Name => _model.Name;
    public SessionPreset Model => _model;

    /// <summary>
    ///     A collection of ViewModels for the configured modules, used to display rich information in the UI.
    /// </summary>
    public ObservableCollection<ConfiguredModuleViewModel> Modules { get; } = new();

    public SessionPresetViewModel(SessionPreset model, IModuleRegistry moduleRegistry)
    {
        _model = model;

        // Create a ViewModel for each module in the preset to get access to display-friendly properties.
        var moduleVms = model.Modules
            .Select(m =>
            {
                var def = moduleRegistry.GetDefinitionById(m.ModuleId);
                return def != null ? new ConfiguredModuleViewModel(def, m, moduleRegistry) : null;
            })
            .Where(vm => vm != null);

        foreach (var vm in moduleVms)
            Modules.Add(vm!);
    }
}