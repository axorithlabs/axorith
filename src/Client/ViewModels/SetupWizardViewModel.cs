using System.Collections.ObjectModel;
using System.Windows.Input;
using Axorith.Client.Services;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class SetupWizardViewModel : ReactiveObject
{
    private readonly IClientOnboardingService _onboardingService;

    public ObservableCollection<PresetTypeOption> PresetTypes { get; } = [];

    public bool IsScanning
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasOptions => PresetTypes.Count > 0;

    public int SelectedCount => PresetTypes.Count(p => p.IsSelected);

    public bool HasSelection => PresetTypes.Any(p => p.IsSelected);

    public ICommand CreatePresetsCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleSelectionCommand { get; }

    public event EventHandler<SetupWizardResult>? Completed;

    public SetupWizardViewModel(IClientOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;

        CreatePresetsCommand = ReactiveCommand.CreateFromTask(CreatePresetsAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ToggleSelectionCommand = ReactiveCommand.Create<PresetTypeOption>(ToggleSelection);
    }

    public async Task ScanForPresetsAsync()
    {
        IsScanning = true;

        try
        {
            var discoveryResult = await _onboardingService.DiscoverAvailablePresetsAsync();

            PresetTypes.Clear();

            if (discoveryResult.HasCodingPresets)
            {
                PresetTypes.Add(new PresetTypeOption
                {
                    Type = "Developer",
                    Title = "Developer / Coder",
                    Description = "Focus presets for coding with IDE launchers and distraction blockers",
                    Icon = "M12.89,3L14.85,3.4L11.11,21L9.15,20.6L12.89,3M19.59,12L16,8.41V5.58L22.42,12L16,18.41V15.58L19.59,12M1.58,12L8,5.58V8.41L4.41,12L8,15.58V18.41L1.58,12Z",
                    IsSelected = false,
                    Count = discoveryResult.CodingPresetCount
                });
            }

            if (discoveryResult.HasGamingPresets)
            {
                PresetTypes.Add(new PresetTypeOption
                {
                    Type = "Gamer",
                    Title = "Gamer",
                    Description = "Gaming sessions with Steam, Discord, and work-site blockers",
                    Icon = "M6,9H8V11H10V13H8V15H6V13H4V11H6V9M18.5,9A1.5,1.5 0 0,1 20,10.5A1.5,1.5 0 0,1 18.5,12A1.5,1.5 0 0,1 17,10.5A1.5,1.5 0 0,1 18.5,9M15.5,12A1.5,1.5 0 0,1 17,13.5A1.5,1.5 0 0,1 15.5,15A1.5,1.5 0 0,1 14,13.5A1.5,1.5 0 0,1 15.5,12M17,6A3,3 0 0,1 20,9V15A3,3 0 0,1 17,18H7A3,3 0 0,1 4,15V9A3,3 0 0,1 7,6H17M7,8A1,1 0 0,0 6,9V15A1,1 0 0,0 7,16H17A1,1 0 0,0 18,15V9A1,1 0 0,0 17,8H7Z",
                    IsSelected = false,
                    Count = discoveryResult.GamingPresetCount
                });
            }

            if (discoveryResult.HasStreamingPresets)
            {
                PresetTypes.Add(new PresetTypeOption
                {
                    Type = "Streamer",
                    Title = "Streamer / Content Creator",
                    Description = "Streaming setup with OBS, browser dashboard, and focus tools",
                    Icon = "M17,10.5V7A1,1 0 0,0 16,6H4A1,1 0 0,0 3,7V17A1,1 0 0,0 4,18H16A1,1 0 0,0 17,17V13.5L21,17.5V6.5L17,10.5Z",
                    IsSelected = false,
                    Count = discoveryResult.StreamingPresetCount
                });
            }

            this.RaisePropertyChanged(nameof(HasOptions));
            this.RaisePropertyChanged(nameof(SelectedCount));
            this.RaisePropertyChanged(nameof(HasSelection));
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task CreatePresetsAsync()
    {
        var selectedTypes = PresetTypes.Where(p => p.IsSelected).Select(p => p.Type).ToList();

        if (selectedTypes.Count == 0)
        {
            Cancel();
            return;
        }

        var result = await _onboardingService.CreateSelectedPresetsAsync(selectedTypes);

        Completed?.Invoke(this, new SetupWizardResult
        {
            Success = result.Success,
            CreatedCount = result.CreatedCount,
            CreatedPresets = result.CreatedPresetNames.ToList()
        });
    }

    private void Cancel()
    {
        Completed?.Invoke(this, new SetupWizardResult { Success = false, CreatedCount = 0 });
    }

    private void ToggleSelection(PresetTypeOption option)
    {
        option.IsSelected = !option.IsSelected;
        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(HasSelection));
    }
}

public class PresetTypeOption : ReactiveObject
{
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public int Count { get; init; }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}

public class SetupWizardResult
{
    public bool Success { get; init; }
    public int CreatedCount { get; init; }
    public List<string> CreatedPresets { get; init; } = [];
}
