using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services;
using Axorith.Client.Services.Abstractions;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Contracts;
using Axorith.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using ReactiveUI;
using Xunit;
using AfterEndBehavior = Axorith.Core.Models.AfterEndBehavior;
using ConfiguredModule = Axorith.Core.Models.ConfiguredModule;
using FocusCommitmentMode = Axorith.Core.Models.FocusCommitmentMode;

namespace Axorith.Integrations.Tests;

public partial class HostGrpcEndToEndTests
{
    [AvaloniaFact]
    public async Task SessionEditorKeepsDelayWhileTypingAndRejectsInvalidDelayWithoutChangingPersistence()
    {
        var clients = await CreateAuthenticatedClientsAsync();
        using var channel = clients.channel;
        var retry = Policy.Handle<Exception>().RetryAsync(0);
        var presets = new GrpcPresetsApi(clients.presets, retry);
        var scheduler = new GrpcSchedulerApi(new SchedulerService.SchedulerServiceClient(channel), retry);
        using var modules = new GrpcModulesApi(clients.modules, retry, NullLogger.Instance);
        using var sessions = new GrpcSessionsApi(clients.sessions, retry, NullLogger.Instance);
        using var toasts = new ToastNotificationService();
        var shell = new ShellViewModel();
        using var services = new ServiceCollection()
            .AddSingleton(shell)
            .AddSingleton<IPresetsApi>(presets)
            .AddSingleton<ISchedulerApi>(scheduler)
            .AddSingleton<IModulesApi>(modules)
            .AddSingleton<ISessionsApi>(sessions)
            .AddSingleton<IUpdatesApi>(new GrpcUpdatesApi(new UpdatesService.UpdatesServiceClient(channel),
                NullLogger<GrpcUpdatesApi>.Instance))
            .AddSingleton<IToastNotificationService>(toasts)
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider();
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(), Name = Guid.NewGuid().ToString(),
            Modules =
            [
                new ConfiguredModule { ModuleId = factory.AppBlockerModuleId },
                new ConfiguredModule { ModuleId = factory.AppBlockerModuleId, StartDelay = TimeSpan.FromSeconds(7) }
            ]
        };
        using var editor = new SessionEditorViewModel(shell, modules, presets, scheduler, toasts, services)
            { PresetToEdit = preset };
        await WaitForEditor(editor, 0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (editor.ConfiguredModules.Any(module => module.IsLoading))
            await Task.Delay(10, deadline.Token);
        Dispatcher.UIThread.RunJobs();
        var delayed = editor.ConfiguredModules[1];
        var view = new SessionEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 1000 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(),
                box => box.Classes.Contains("delay-input") && box.IsEffectivelyVisible);
            Assert.True(input.Focus());
            Assert.True(editor.SaveAndCloseCommand.CanExecute(null));

            foreach (var existing in new[] { false, true })
            {
                if (existing) await presets.CreatePresetAsync(preset);
                foreach (var invalid in new[] { "", " ", "0", "-1", "abc", "NaN", "∞" })
                {
                    input.SelectAll();
                    window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
                    window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
                    if (invalid.Length > 0) window.KeyTextInput(invalid);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(invalid, delayed.StartDelaySecondsString);
                    Assert.True(delayed.HasDelay);
                    Assert.True(input.IsEffectivelyVisible);
                    Assert.True(input.IsFocused);
                    Assert.Contains("error", input.Classes);
                    var inputBorder = Assert.Single(input.GetVisualDescendants().OfType<Border>(),
                        border => border.Name == "PART_BorderElement");
                    Assert.Equal(Color.Parse("#FF4444"), ((ISolidColorBrush)inputBorder.BorderBrush!).Color);
                    Assert.True(editor.HasValidationErrors);
                    Assert.False(editor.SaveAndCloseCommand.CanExecute(null));
                    await ((ReactiveCommand<Unit, Unit>)editor.SaveAndCloseCommand).Execute();
                    var persisted = await ReadPersistedDelayPresetAsync(preset.Id);
                    if (existing) Assert.Equal(TimeSpan.FromSeconds(7), persisted!.Modules[1].StartDelay);
                    else Assert.Null(persisted);
                    Assert.Equal(2, editor.ConfiguredModules.Count);
                }
                input.Text = "7";
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("error", input.Classes);
                Assert.False(editor.HasValidationErrors);
            }

            input.Text = "12";
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.SaveAndCloseCommand.CanExecute(null));
            await ((ReactiveCommand<Unit, Unit>)editor.SaveAndCloseCommand).Execute();
            Assert.True(string.IsNullOrEmpty(editor.ErrorMessage), editor.ErrorMessage);
            var saved = Assert.IsType<SessionPreset>(await ReadPersistedDelayPresetAsync(preset.Id));
            Assert.Equal(TimeSpan.FromSeconds(12), saved.Modules[1].StartDelay);
            Assert.Equal(TimeSpan.Zero, saved.Modules[0].StartDelay);
            using var reopened = new SessionEditorViewModel(shell, modules, presets, scheduler, toasts, services)
                { PresetToEdit = saved };
            await WaitForEditor(reopened, 0);
            var reopenedDelay = reopened.ConfiguredModules[1];
            Assert.True(reopenedDelay.HasDelay);
            Assert.Equal("12", reopenedDelay.StartDelaySecondsString);
            await reopened.ConfiguredModules[0].RemoveDelayCommand.Execute();
            await reopened.ConfiguredModules[0].AddDelayCommand.Execute();
            Assert.True(reopenedDelay.HasDelay);
            Assert.Equal("1", reopenedDelay.StartDelaySecondsString);
            Assert.Equal(TimeSpan.FromSeconds(1), reopenedDelay.Model.StartDelay);
            reopenedDelay.StartDelaySecondsString = "";
            await reopened.ConfiguredModules[0].RemoveDelayCommand.Execute();
            Dispatcher.UIThread.RunJobs();
            Assert.False(reopenedDelay.HasDelay);
            Assert.False(reopened.HasValidationErrors);
            await ((ReactiveCommand<Unit, Unit>)reopened.SaveAndCloseCommand).Execute();
            Assert.True(string.IsNullOrEmpty(reopened.ErrorMessage), reopened.ErrorMessage);
            Assert.Equal(TimeSpan.Zero, (await ReadPersistedDelayPresetAsync(preset.Id))!.Modules[1].StartDelay);
        }
        finally { window.Close(); }
    }

    private Task<SessionPreset?> ReadPersistedDelayPresetAsync(Guid id) =>
        new Axorith.Core.Services.PresetManager(Path.Combine(factory.TestDataPath, "presets"),
            NullLogger<Axorith.Core.Services.PresetManager>.Instance).GetPresetByIdAsync(id, CancellationToken.None);

    [AvaloniaFact]
    public async Task SessionEditorPreservesThenActionsAndSchedulesAcrossRepeatedSaves()
    {
        var clients = await CreateAuthenticatedClientsAsync();
        using var channel = clients.channel;
        var retry = Policy.Handle<Exception>().RetryAsync(0);
        var presets = new GrpcPresetsApi(clients.presets, retry);
        var scheduler = new GrpcSchedulerApi(new SchedulerService.SchedulerServiceClient(channel), retry);
        using var modules = new GrpcModulesApi(clients.modules, retry, NullLogger.Instance);
        using var sessions = new GrpcSessionsApi(clients.sessions, retry, NullLogger.Instance);
        using var toasts = new ToastNotificationService();
        var shell = new ShellViewModel();
        using var services = new ServiceCollection()
            .AddSingleton(shell)
            .AddSingleton<IPresetsApi>(presets)
            .AddSingleton<ISchedulerApi>(scheduler)
            .AddSingleton<IModulesApi>(modules)
            .AddSingleton<ISessionsApi>(sessions)
            .AddSingleton<IUpdatesApi>(new GrpcUpdatesApi(new UpdatesService.UpdatesServiceClient(channel),
                NullLogger<GrpcUpdatesApi>.Instance))
            .AddSingleton<IToastNotificationService>(toasts)
            .AddSingleton<MainViewModel>()
            .BuildServiceProvider();
        var next = await presets.CreatePresetAsync(new SessionPreset { Id = Guid.NewGuid(), Name = Guid.NewGuid().ToString() });

        foreach (var behavior in Enum.GetValues<AfterEndBehavior>().Where(value => value != AfterEndBehavior.DoNothing))
        {
            var preset = new SessionPreset { Id = Guid.NewGuid(), Name = Guid.NewGuid().ToString() };
            var mode = (FocusCommitmentMode)((int)behavior % 3);
            using (var editor = new SessionEditorViewModel(shell, modules, presets, scheduler, toasts, services)
                   { PresetToEdit = preset })
            {
                await WaitForEditor(editor, 0);
                await editor.AddStopAfterDurationTriggerCommand.Execute();
                var duration = Assert.IsType<StopAfterDurationTriggerViewModel>(Assert.Single(editor.StopTriggers));
                duration.DurationHours = 2;
                duration.DurationMinutes = 17;
                editor.FocusCommitmentModeIndex = (int)mode;
                editor.BreakPresetIndex = 2;
                editor.BreakCount = 3;
                editor.BreakDurationMinutes = 7;
                await editor.AddScheduleTriggerCommand.Execute();
                var start = Assert.IsType<ScheduleTriggerViewModel>(Assert.Single(editor.Triggers));
                start.Time = new TimeSpan(14, 23, 0);
                start.Use24HourFormat = false;
                await editor.AddThenActionTriggerCommand.Execute(behavior);
                var action = Assert.IsType<ThenActionTriggerViewModel>(Assert.Single(editor.ThenTriggers));
                if (behavior == AfterEndBehavior.StartNextWorkspace)
                    action.SelectedNextPreset = editor.AvailablePresetsForNext.Single(option => option.PresetId == next.Id);
                Dispatcher.UIThread.RunJobs();
                await ((ReactiveCommand<Unit, Unit>)editor.SaveAndCloseCommand).Execute();
                Assert.True(string.IsNullOrEmpty(editor.ErrorMessage), editor.ErrorMessage);
            }

            if (behavior == AfterEndBehavior.LockPc)
            {
                var start = (await scheduler.ListSchedulesAsync()).Single(schedule =>
                    schedule.PresetId == preset.Id && schedule.Type == ScheduleType.Recurring);
                await scheduler.SetEnabledAsync(start.Id, false);
                await scheduler.CreateScheduleAsync(new SessionSchedule
                {
                    PresetId = preset.Id, Name = "Keep one-time schedule", Type = ScheduleType.OneTime,
                    OneTimeDate = DateTimeOffset.Now.AddDays(2)
                });
            }
            var originalSchedules = (await scheduler.ListSchedulesAsync()).Where(schedule => schedule.PresetId == preset.Id).ToArray();
            Assert.Equal(behavior == AfterEndBehavior.LockPc ? 3 : 2, originalSchedules.Length);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var loaded = await presets.GetPresetAsync(preset.Id);
                Assert.NotNull(loaded);
                Assert.Equal(behavior, loaded.FocusCommitment.AfterEnd);
                Assert.Equal(mode, loaded.FocusCommitment.Mode);
                Assert.Equal(3, loaded.FocusCommitment.BreakCount);
                Assert.Equal(TimeSpan.FromMinutes(7), loaded.FocusCommitment.BreakDuration);
                using var editor = new SessionEditorViewModel(shell, modules, presets, scheduler, toasts, services)
                    { PresetToEdit = loaded };
                editor.PresetToEdit = next;
                editor.PresetToEdit = loaded;
                await WaitForEditor(editor, 1);
                var action = Assert.IsType<ThenActionTriggerViewModel>(Assert.Single(editor.ThenTriggers));
                Assert.Equal(behavior, action.Behavior);
                Assert.Equal(behavior == AfterEndBehavior.StartNextWorkspace ? next.Id : (Guid?)null, action.NextPresetId);
                var duration = Assert.IsType<StopAfterDurationTriggerViewModel>(Assert.Single(editor.StopTriggers));
                Assert.Equal(TimeSpan.FromMinutes(137), duration.Duration);
                var start = Assert.IsType<ScheduleTriggerViewModel>(Assert.Single(editor.Triggers));
                Assert.Equal(new TimeSpan(14, 23, 0), start.Time);
                Assert.False(start.Use24HourFormat);
                await ((ReactiveCommand<Unit, Unit>)editor.SaveAndCloseCommand).Execute();
                Assert.True(string.IsNullOrEmpty(editor.ErrorMessage), editor.ErrorMessage);
                var savedSchedules = (await scheduler.ListSchedulesAsync()).Where(schedule => schedule.PresetId == preset.Id).ToArray();
                Assert.Equal(originalSchedules.Select(schedule => schedule.Id).Order(), savedSchedules.Select(schedule => schedule.Id).Order());
                Assert.All(originalSchedules, original => Assert.Equal(original.IsEnabled,
                    savedSchedules.Single(saved => saved.Id == original.Id).IsEnabled));
            }

            using (var editor = new SessionEditorViewModel(shell, modules, presets, scheduler, toasts, services)
                   { PresetToEdit = await presets.GetPresetAsync(preset.Id) })
            {
                await WaitForEditor(editor, 1);
                editor.RemoveThenTriggerCommand.Execute(Assert.Single(editor.ThenTriggers));
                await ((ReactiveCommand<Unit, Unit>)editor.SaveAndCloseCommand).Execute();
                Assert.True(string.IsNullOrEmpty(editor.ErrorMessage), editor.ErrorMessage);
                editor.FocusCommitmentModeIndex = (int)FocusCommitmentMode.Normal;
                editor.RemoveStopTriggerCommand.Execute(Assert.Single(editor.StopTriggers));
                editor.RemoveTriggerCommand.Execute(Assert.Single(editor.Triggers));
                Dispatcher.UIThread.RunJobs();
                await ((ReactiveCommand<Unit, Unit>)editor.SaveAndCloseCommand).Execute();
                Assert.True(string.IsNullOrEmpty(editor.ErrorMessage), editor.ErrorMessage);
            }
            Assert.Equal(AfterEndBehavior.DoNothing, (await presets.GetPresetAsync(preset.Id))!.FocusCommitment.AfterEnd);
            Assert.All((await scheduler.ListSchedulesAsync()).Where(schedule => schedule.PresetId == preset.Id),
                schedule => Assert.Equal(ScheduleType.OneTime, schedule.Type));
        }

        var protectedPreset = await presets.CreatePresetAsync(new SessionPreset
        {
            Id = Guid.NewGuid(), Name = Guid.NewGuid().ToString(),
            FocusCommitment = new Axorith.Core.Models.FocusCommitmentOptions { AfterEnd = AfterEndBehavior.LockPc }
        });
        await scheduler.CreateScheduleAsync(new SessionSchedule
        {
            PresetId = protectedPreset.Id, Name = "Keep duration", Type = ScheduleType.StopDuration,
            AutoStopDuration = TimeSpan.FromMinutes(30)
        });
        using var failedClient = factory.CreateDefaultClient(new RejectScheduleLoadsHandler());
        using var failedChannel = TestGrpc.CreateAuthenticatedChannel(failedClient,
            File.ReadAllText(Path.Combine(factory.TestDataPath, "config", ".auth_token")));
        var failedScheduler = new GrpcSchedulerApi(new SchedulerService.SchedulerServiceClient(failedChannel), retry);
        using var failedEditor = new SessionEditorViewModel(shell, modules, presets, failedScheduler, toasts, services)
            { PresetToEdit = protectedPreset };
        await failedEditor.InitializationTask;
        Dispatcher.UIThread.RunJobs();
        await ((ReactiveCommand<Unit, Unit>)failedEditor.SaveAndCloseCommand).Execute();
        Assert.False(string.IsNullOrWhiteSpace(failedEditor.ErrorMessage));
        Assert.Equal(AfterEndBehavior.LockPc, (await presets.GetPresetAsync(protectedPreset.Id))!.FocusCommitment.AfterEnd);
        Assert.Single(await scheduler.ListSchedulesAsync(), schedule => schedule.PresetId == protectedPreset.Id);
    }

    private sealed class RejectScheduleLoadsHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("/ListSchedules", StringComparison.Ordinal)
                ? throw new HttpRequestException("Schedule service unavailable")
                : base.SendAsync(request, cancellationToken);
    }

    private static async Task WaitForEditor(SessionEditorViewModel editor, int scheduleCount)
    {
        await editor.InitializationTask;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (editor.StopTriggers.Count != scheduleCount)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, timeout.Token);
        }
        // Let superseded schedule responses reach the UI before checking for duplicate triggers.
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs();
    }
}
