using System.Windows.Input;
using Axorith.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class SettingsViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IServiceProvider _serviceProvider;
    private readonly IClientUiSettingsStore _uiSettingsStore;
    private readonly Configuration _configuration;

    public bool MinimizeToTrayOnClose
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public SettingsViewModel(
        ShellViewModel shell,
        IServiceProvider serviceProvider,
        IClientUiSettingsStore uiSettingsStore,
        IOptions<Configuration> clientConfigurationOptions)
    {
        _shell = shell;
        _serviceProvider = serviceProvider;
        _uiSettingsStore = uiSettingsStore;
        _configuration = clientConfigurationOptions.Value;

        MinimizeToTrayOnClose = _configuration.Ui.MinimizeToTrayOnClose;

        SaveCommand = ReactiveCommand.Create(Save);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    private void Save()
    {
        _configuration.Ui.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        _uiSettingsStore.Save(_configuration.Ui);
        NavigateBack();
    }

    private void Cancel()
    {
        NavigateBack();
    }

    private void NavigateBack()
    {
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        _shell.NavigateTo(mainViewModel);
    }
}
