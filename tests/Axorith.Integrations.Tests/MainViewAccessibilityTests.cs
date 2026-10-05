using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ReactiveUI;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class MainViewAccessibilityTests
{
    [AvaloniaFact]
    public async Task StartReviewKeepsFocusInDialogAndDisablesBackground()
    {
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Focus",
            FocusCommitment = new FocusCommitmentOptions
            {
                Mode = FocusCommitmentMode.Locked,
                EndCondition = FocusEndCondition.Duration,
                Duration = TimeSpan.FromHours(2)
            }
        };
        var sessionsApi = new Mock<ISessionsApi>();
        sessionsApi.Setup(api => api.SessionEvents).Returns(Observable.Empty<SessionEvent>());
        sessionsApi.Setup(api => api.PreflightSessionAsync(preset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, "ready"));
        using var services = new ServiceCollection().BuildServiceProvider();
        using var viewModel = new MainViewModel(new ShellViewModel(), null!, sessionsApi.Object, null!, services);
        using var presetViewModel = new SessionPresetViewModel(preset, [], null!, services);
        var view = new MainView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            await ((ReactiveCommand<SessionPresetViewModel, Unit>)viewModel.StartSelectedCommand)
                .Execute(presetViewModel).ToTask();
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsStartConfirmationOpen);
            Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
            Assert.False(viewModel.StartSelectedCommand.CanExecute(null));
            Assert.False(viewModel.OpenSettingsCommand.CanExecute(null));
            Assert.Equal(KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation(view.FindControl<StackPanel>("StartDialogContent")!));
            Assert.Same(view.FindControl<Button>("StartConfirmationCancelButton"),
                window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }
}
