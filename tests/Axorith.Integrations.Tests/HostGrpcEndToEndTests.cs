using Autofac;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Axorith.Client.CoreSdk;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly;
using System.Runtime.CompilerServices;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

[assembly: AvaloniaTestApplication(typeof(Axorith.Integrations.Tests.HostGrpcEndToEndTests.TestAppBuilder))]

namespace Axorith.Integrations.Tests;

public sealed class HostTestFactory : WebApplicationFactory<Program>
{
    public string TestDataPath { get; }

    public HostTestFactory()
    {
        TestDataPath = Path.Combine(Path.GetTempPath(), "AxorithTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestDataPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var testConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:PresetsPath"] = Path.Combine(TestDataPath, "presets"),
                    ["Persistence:LogsPath"] = Path.Combine(TestDataPath, "logs"),
                    ["Persistence:ConfigPath"] = Path.Combine(TestDataPath, "config"),
                    ["Modules:SearchPaths:0"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:1"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:2"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:3"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:4"] = Path.Combine(TestDataPath, "empty_modules")
                })
                .Build();

            configBuilder.AddConfiguration(testConfig);
        });

        builder.ConfigureTestContainer<ContainerBuilder>(containerBuilder =>
        {
            var mockRegistry = new Mock<IModuleRegistry>();

            var testModules = new List<ModuleDefinition>
            {
                new()
                {
                    Id = Guid.NewGuid(), Name = "System Module", Category = "System", Platforms = [Platform.Windows]
                },
                new()
                {
                    Id = Guid.NewGuid(), Name = "Music Module", Category = "Music", Platforms = [Platform.Windows]
                },
                new()
                {
                    Id = Guid.NewGuid(), Name = "Dev Module", Category = "Development", Platforms = [Platform.Windows]
                }
            };

            mockRegistry.Setup(r => r.GetAllDefinitions()).Returns(testModules);

            mockRegistry.Setup(r => r.GetDefinitionById(It.IsAny<Guid>()))
                .Returns((Guid id) => testModules.FirstOrDefault(m => m.Id == id));

            containerBuilder.RegisterInstance(mockRegistry.Object)
                .As<IModuleRegistry>()
                .SingleInstance();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(TestDataPath))
            {
                Directory.Delete(TestDataPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

public class HostGrpcEndToEndTests(HostTestFactory factory) : IClassFixture<HostTestFactory>
{
    [AvaloniaFact]
    public async Task MainViewKeepsIdleAndSidebarActionsClearAndReadable()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using var channelLifetime = channel;
        using var api = new GrpcSessionsApi(sessions, Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var viewModel = new MainViewModel(null!, null!, api, null!, services);
        var view = new MainView { DataContext = viewModel };

        Assert.False(viewModel.IsSessionActive);
        Assert.Null(view.FindControl<Border>("SessionStatusNotice"));
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text == "No active session");

        foreach (var name in new[] { "HomeSidebarButton", "PresetsSidebarButton", "SettingsSidebarButton" })
        {
            Assert.Equal(HorizontalAlignment.Stretch, view.FindControl<Button>(name)!.HorizontalAlignment);
        }
        Assert.Equal(12, view.FindControl<Button>("HomeSidebarButton")!.FontSize);

        var updateButton = view.FindControl<Button>("SidebarUpdateButton")!;
        var sidebar = view.FindControl<Grid>("SidebarNavigationGrid")!;
        Assert.False(updateButton.IsVisible);
        Assert.Contains(updateButton, sidebar.Children);
        Assert.Equal(4, Grid.GetRow(updateButton));

        typeof(MainViewModel).GetProperty(nameof(MainViewModel.UpdateAvailable))!
            .SetValue(viewModel, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(updateButton.IsVisible);
        Assert.Same(viewModel.InstallUpdateCommand, updateButton.Command);

        typeof(MainViewModel).GetProperty(nameof(MainViewModel.UpdateAvailable))!
            .SetValue(viewModel, false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(updateButton.IsVisible);

        Assert.Contains(view.FindControl<Button>("CreateSessionButton"),
            view.FindControl<Grid>("PresetsHeader")!.Children);
        Assert.Equal(16, new SettingsView().FindControl<TextBlock>("SettingsSidebarHeading")!.FontSize);

        using var preset = new SessionPresetViewModel(new SessionPreset { Name = "Deep Work" },
            [], null!, services);
        var card = view.FindControl<ListBox>("PresetsListBox")!.ItemTemplate!.Build(preset)!;
        var moreButton = card.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "PresetActionsButton");
        Assert.Equal(40, moreButton.Width);
        Assert.Equal(3, Assert.IsType<StackPanel>(moreButton.Content).Children.Count);

        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsSessionActive))!
            .SetValue(viewModel, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Button>("SidebarStopSessionButton")!.IsVisible);
    }

    static HostGrpcEndToEndTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    private async Task<(
        DiagnosticsService.DiagnosticsServiceClient diagnostics,
        PresetsService.PresetsServiceClient presets,
        SessionsService.SessionsServiceClient sessions,
        ModulesService.ModulesServiceClient modules,
        GrpcChannel channel)> CreateAuthenticatedClientsAsync()
    {
        var httpClient = factory.CreateDefaultClient();

        var tokenPath = Path.Combine(factory.TestDataPath, "config", ".auth_token");

        var token = string.Empty;
        for (var i = 0; i < 50; i++)
        {
            if (File.Exists(tokenPath))
            {
                try
                {
                    using var fs = new FileStream(tokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    token = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        break;
                    }
                }
                catch
                {
                    /* ignore and retry */
                }
            }

            await Task.Delay(100);
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new FileNotFoundException(
                $"Auth token not found at {tokenPath}. Host failed to start or write token.");
        }

        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            metadata.Add("x-axorith-auth-token", token);
            return Task.CompletedTask;
        });

        var channelOptions = new GrpcChannelOptions
        {
            HttpClient = httpClient,
            Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials),
            UnsafeUseInsecureChannelCallCredentials = true
        };

        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, channelOptions);

        return (
            new DiagnosticsService.DiagnosticsServiceClient(channel),
            new PresetsService.PresetsServiceClient(channel),
            new SessionsService.SessionsServiceClient(channel),
            new ModulesService.ModulesServiceClient(channel),
            channel
        );
    }

    [Fact]
    public async Task Diagnostics_GetHealth_ShouldReturnHealthy()
    {
        var (diagnostics, _, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var response = await diagnostics.GetHealthAsync(new HealthCheckRequest());

            response.Should().NotBeNull();
            response.Status.Should().Be(HealthStatus.Healthy);
            response.Version.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Presets_Create_List_Get_Delete_ShouldRoundTrip()
    {
        var (_, presets, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var name = $"IntegrationTest-{Guid.NewGuid():N}";
            var endDays = new[] { DayOfWeek.Monday, DayOfWeek.Friday };

            var created = await presets.CreatePresetAsync(new CreatePresetRequest
            {
                Preset = new Preset
                {
                    Name = name,
                    FocusCommitment = new Axorith.Contracts.FocusCommitmentOptions
                    {
                        Mode = (Axorith.Contracts.FocusCommitmentMode)1,
                        EndCondition = (Axorith.Contracts.FocusEndCondition)2,
                        HasEndAtLocalTime = true,
                        EndAtHour = 18,
                        EndAtMinute = 30,
                        AfterEnd = (Axorith.Contracts.AfterEndBehavior)0,
                        EndAtDaysOfWeek = { endDays.Select(day => (int)day) }
                    }
                }
            });

            created.Should().NotBeNull();
            created.Name.Should().Be(name);
            Guid.TryParse(created.Id, out _).Should().BeTrue();

            var list = await presets.ListPresetsAsync(new ListPresetsRequest());
            list.Presets.Should().NotBeNull();
            list.Presets.Should().Contain(p => p.Id == created.Id);

            var fetched = await presets.GetPresetAsync(new GetPresetRequest
            {
                PresetId = created.Id
            });

            fetched.Should().NotBeNull();
            fetched.Id.Should().Be(created.Id);
            fetched.Name.Should().Be(name);
            fetched.FocusCommitment.EndAtDaysOfWeek.Should().Equal(endDays.Select(day => (int)day));

            await presets.DeletePresetAsync(new DeletePresetRequest
            {
                PresetId = created.Id
            });

            var afterDelete = await presets.ListPresetsAsync(new ListPresetsRequest());
            afterDelete.Presets.Should().NotContain(p => p.Id == created.Id);
        }
    }

    [Fact]
    public async Task Presets_GetPreset_WithInvalidId_ShouldReturnRpcInvalidArgument()
    {
        var (_, presets, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var act = async () =>
                await presets.GetPresetAsync(new GetPresetRequest
                {
                    PresetId = "invalid-guid"
                });

            var exception = await Assert.ThrowsAsync<RpcException>(act);
            exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        }
    }

    [Fact]
    public async Task Sessions_GetSessionState_WhenNoActiveSession_ShouldReturnInactive()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());

            state.Should().NotBeNull();
            state.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Sessions_StartSession_WithInvalidPresetId_ShouldReturnFailure()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var response = await sessions.StartSessionAsync(new StartSessionRequest
            {
                PresetId = "invalid-guid"
            });

            response.Should().NotBeNull();
            response.Success.Should().BeFalse();
            response.Message.Should().Contain("Invalid preset ID");
        }
    }

    [Fact]
    public async Task Sessions_StartSession_WithUnknownPreset_ShouldReturnFailure()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var presetId = Guid.NewGuid().ToString();

            var response = await sessions.StartSessionAsync(new StartSessionRequest
            {
                PresetId = presetId
            });

            response.Should().NotBeNull();
            response.Success.Should().BeFalse();
            response.Message.Should().Contain("Preset not found");
        }
    }

    [Fact]
    public async Task EmergencyUnlockStream_ReturnsWhenHostRejectsHold()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var api = new GrpcSessionsApi(sessions, Policy.Handle<Exception>().RetryAsync(0),
                   NullLogger.Instance))
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await using var progress = api.HoldEmergencyUnlockAsync(HeldSignals(timeout.Token), timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.True(await progress.MoveNextAsync());
            Assert.Contains("no committed session", progress.Current.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(await progress.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        }
    }

    private static async IAsyncEnumerable<bool> HeldSignals(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return true;
            await Task.Delay(100, cancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task FrontendInteractionsAndSessionEditorLayoutStayConsistent()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var api = new GrpcSessionsApi(sessions, Policy.Handle<Exception>().RetryAsync(0),
                   NullLogger.Instance))
        using (var services = new ServiceCollection().BuildServiceProvider())
        using (var viewModel = new MainViewModel(null!, null!, api, null!, services))
        {
            using var editorViewModel = new SessionEditorViewModel(null!, null!, null!, null!, null!, services);
            Dispatcher.UIThread.RunJobs();
            await editorViewModel.InitializationTask;
            var editorView = new SessionEditorView { DataContext = editorViewModel };
            var focusCard = editorView.FindControl<Border>("FocusCommitmentCard")!;
            Assert.Equal(40, focusCard.Margin.Top);
            focusCard.Measure(new Size(800, 1200));
            Assert.Equal(500, focusCard.DesiredSize.Width);
            Assert.False(editorView.FindControl<StackPanel>("ScheduleLockPanel")!.IsVisible);
            Assert.Null(editorView.FindControl<StackPanel>("CommittedEndConditionPanel"));
            Assert.Null(editorView.FindControl<StackPanel>("CommittedAfterEndPanel"));
            Assert.Empty(editorViewModel.StopTriggers);
            Assert.Empty(editorViewModel.ThenTriggers);
            Assert.Equal("Stop On...", editorView.FindControl<TextBlock>("StopOnHeading")!.Text);
            Assert.Equal("Then...", editorView.FindControl<TextBlock>("ThenHeading")!.Text);
            Assert.NotNull(editorView.FindControl<Button>("AddStopTriggerButton"));
            Assert.NotNull(editorView.FindControl<Button>("AddThenTriggerButton"));

            var thenCard = editorView.FindControl<Border>("ThenCard")!;
            var thenAction = new ThenActionTriggerViewModel(editorViewModel,
                Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace);
            editorViewModel.ThenTriggers.Add(thenAction);
            var thenItems = thenCard.GetVisualDescendants().OfType<ItemsControl>().Single();
            var thenActionCard = (Border)thenItems.ItemTemplate!.Build(thenAction)!;
            thenActionCard.DataContext = thenAction;
            var thenCardContent = thenCard.Child;
            thenCard.Child = thenActionCard;
            var editorWindow = new Window { Content = editorView, Width = 800, Height = 800 };
            try
            {
                editorWindow.Show();
                Dispatcher.UIThread.RunJobs();
                thenCard.Measure(new Size(800, 1200));
                thenCard.Arrange(new Rect(0, 0, 800, 1200));
                Assert.Equal(Color.Parse("#111"), ((ISolidColorBrush)thenActionCard.Background!).Color);
                Assert.Equal(Color.Parse("#FF6B6B"), ((ISolidColorBrush)thenActionCard.BorderBrush!).Color);
                Assert.Single(thenCard.GetVisualDescendants().OfType<TextBlock>(),
                    label => label.IsVisible && label.Text == "No other Workspaces are available.");
                Assert.DoesNotContain(thenCard.GetVisualDescendants().OfType<TextBlock>(),
                    label => label.IsVisible && label.Text == "Choose a Workspace");
            }
            finally
            {
                editorWindow.Close();
                thenCard.Child = thenCardContent;
            }
            editorViewModel.ThenTriggers.Clear();

            editorViewModel.FocusCommitmentModeIndex = (int)Axorith.Core.Models.FocusCommitmentMode.Strict;
            focusCard.Measure(new Size(800, 1200));
            Assert.Equal(500, focusCard.DesiredSize.Width);

            var validateCommitment = typeof(SessionEditorViewModel).GetMethod("ValidateFocusCommitment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.False((bool)validateCommitment.Invoke(editorViewModel, null)!);
            Assert.Contains("Add one Stop Trigger", editorViewModel.ErrorMessage ?? string.Empty);

            editorViewModel.StopTriggers.Add(new StopAfterDurationTriggerViewModel
                { Duration = TimeSpan.FromMinutes(45) });
            Assert.True((bool)validateCommitment.Invoke(editorViewModel, null)!);
            var options = (Axorith.Core.Models.FocusCommitmentOptions)typeof(SessionEditorViewModel)
                .GetField("_focusCommitment", System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.NonPublic)!
                .GetValue(editorViewModel)!;
            Assert.Equal(Axorith.Core.Models.FocusEndCondition.Duration, options.EndCondition);
            Assert.Equal(TimeSpan.FromMinutes(45), options.Duration);
            Assert.Equal(Axorith.Core.Models.AfterEndBehavior.DoNothing, options.AfterEnd);

            editorViewModel.StopTriggers.Clear();
            editorViewModel.StopTriggers.Add(new StopAtTimeTriggerViewModel
            {
                Time = new TimeSpan(18, 30, 0),
                RunOnMonday = true,
                RunOnTuesday = false,
                RunOnWednesday = false,
                RunOnThursday = false,
                RunOnFriday = true,
                RunOnSaturday = false,
                RunOnSunday = false
            });
            var nextPresetId = Guid.NewGuid();
            editorViewModel.ThenTriggers.Add(new ThenActionTriggerViewModel(editorViewModel,
                Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace) { NextPresetId = nextPresetId });
            Assert.True((bool)validateCommitment.Invoke(editorViewModel, null)!);
            Assert.Equal(Axorith.Core.Models.FocusEndCondition.EndAt, options.EndCondition);
            Assert.Equal(new TimeOnly(18, 30), options.EndAtLocalTime);
            Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, options.EndAtDaysOfWeek);
            Assert.Equal(Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace, options.AfterEnd);
            Assert.Equal(nextPresetId, options.NextWorkspaceId);

            editorViewModel.ThenTriggers.Clear();
            editorViewModel.ThenTriggers.Add(new ThenActionTriggerViewModel(editorViewModel,
                Axorith.Core.Models.AfterEndBehavior.ShutDownPc));
            Assert.True((bool)validateCommitment.Invoke(editorViewModel, null)!);
            Assert.Equal(Axorith.Core.Models.AfterEndBehavior.ShutDownPc, options.AfterEnd);
            Assert.Null(options.NextWorkspaceId);

            foreach (var (behavior, title) in new[]
                     {
                         (Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace, "Start next Workspace"),
                         (Axorith.Core.Models.AfterEndBehavior.LockPc, "Lock PC"),
                         (Axorith.Core.Models.AfterEndBehavior.Sleep, "Sleep"),
                         (Axorith.Core.Models.AfterEndBehavior.SignOut, "Sign out"),
                         (Axorith.Core.Models.AfterEndBehavior.ShutDownPc, "Shut down PC")
                     })
            {
                editorViewModel.ThenTriggers.Clear();
                var action = new ThenActionTriggerViewModel(editorViewModel, behavior);
                editorViewModel.ThenTriggers.Add(action);
                Assert.Equal(title, action.Title);
                if (behavior == Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace)
                {
                    action.NextPresetId = nextPresetId;
                }
                Assert.True((bool)validateCommitment.Invoke(editorViewModel, null)!);
                Assert.Equal(behavior, options.AfterEnd);
            }

            var resolveEndAt = typeof(Axorith.Core.Services.SessionManager)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Single(method => method.Name == "ResolveEndAt" && method.GetParameters().Length == 3);
            var resolvedEnd = (DateTimeOffset)resolveEndAt.Invoke(null,
                [options.EndAtLocalTime!.Value, options.EndAtDaysOfWeek,
                    new DateTimeOffset(2024, 1, 1, 19, 0, 0, TimeSpan.FromHours(3))])!;
            Assert.Equal(new DateTimeOffset(2024, 1, 5, 15, 30, 0, TimeSpan.Zero), resolvedEnd);

            editorViewModel.Triggers.Add(new ScheduleTriggerViewModel());
            Assert.True(editorView.FindControl<StackPanel>("ScheduleLockPanel")!.IsVisible);
            editorViewModel.Triggers.Clear();
            Assert.False(editorView.FindControl<StackPanel>("ScheduleLockPanel")!.IsVisible);

            var view = new MainView { DataContext = viewModel };
            var mainContent = Assert.IsType<Grid>(view.Content).Children.OfType<Grid>()
                .Single(grid => Grid.GetColumn(grid) == 1);
            var homeContent = view.FindControl<ScrollViewer>("HomeScrollViewer")!;
            Assert.True(viewModel.CreateSessionCommand.CanExecute(null));
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, true);

            Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
            Assert.False(viewModel.StartSelectedCommand.CanExecute(null));
            Assert.False(viewModel.OpenSettingsCommand.CanExecute(null));
            Assert.False(mainContent.IsEnabled);

            view.Measure(new Size(320, 300));
            Assert.True(view.FindControl<Border>("StartDialogCard")!.DesiredSize.Width <= 320);
            Assert.True(view.FindControl<ScrollViewer>("StartDialogScrollViewer")!.DesiredSize.Height <= 300);
            Assert.Equal(KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation(view.FindControl<StackPanel>("StartDialogContent")!));

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, true);
            Assert.False(mainContent.IsEnabled);
            Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
            view.Measure(new Size(320, 300));
            Assert.True(view.FindControl<Border>("EmergencyDialogCard")!.DesiredSize.Width <= 320);
            Assert.Equal(KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation(view.FindControl<StackPanel>("EmergencyDialogContent")!));

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsSessionActive))!
                .SetValue(viewModel, true);
            Assert.Contains(homeContent.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == viewModel.ActiveWorkspaceName && text.IsVisible);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, true);
            Assert.Equal(720, view.FindControl<Border>("StartDialogCard")!.MaxWidth);
            Assert.Equal(HorizontalAlignment.Stretch,
                view.FindControl<Border>("StartDialogCard")!.HorizontalAlignment);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, true);

            using var preset = new SessionPresetViewModel(new SessionPreset { Name = "Deep Work" },
                [], null!, services);
            var card = view.FindControl<ListBox>("PresetsListBox")!.ItemTemplate!.Build(preset)!;
            var actions = card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "PresetActionsButton");
            Assert.Equal(1, actions.Opacity);

            var editor = new SessionEditorView();
            foreach (var templateName in new[] { "FilePickerSettingTemplate", "DirectoryPickerSettingTemplate" })
            {
                var picker = ((IDataTemplate)editor.Resources[templateName]!).Build(null)!;
                var history = picker.GetVisualDescendants().OfType<ComboBox>().Single();
                var historyItem = history.ItemTemplate!.Build("Recent path")!;
                var remove = historyItem.GetVisualDescendants().OfType<Button>().Single();
                Assert.Equal(1, remove.Opacity);
                Assert.True(remove.IsHitTestVisible);
            }

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveProtectionStatus))!
                .SetValue(viewModel, "Protection failed");
            var protectionStatus = homeContent.GetLogicalDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Protection failed");
            Assert.Equal(Brushes.IndianRed, protectionStatus.Foreground);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveProtectionStatus))!
                .SetValue(viewModel, "Protection active");
            Assert.Equal(Brushes.LightGreen, protectionStatus.Foreground);
            Assert.False(protectionStatus.IsVisible);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveProtectionStatus))!
                .SetValue(viewModel, "Protection inactive");
            Assert.Equal(Brushes.Gray, protectionStatus.Foreground);
            Assert.False(protectionStatus.IsVisible);

            var window = new Window { Content = view, Width = 900, Height = 700 };
            try
            {
                typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveFocusCommitmentMode))!
                    .SetValue(viewModel, Axorith.Core.Models.FocusCommitmentMode.Locked);
                typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsSessionActive))!
                    .SetValue(viewModel, true);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(view.FindControl<Button>("EmergencyUnlockHoldButton"),
                    window.FocusManager?.GetFocusedElement());
                var holdButton = view.FindControl<Button>("EmergencyUnlockHoldButton")!;
                var pointer = new Pointer(42, PointerType.Mouse, true);
                var pressed = new PointerPressedEventArgs(holdButton, pointer, window, new Point(1, 1), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton,
                        PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1)
                {
                    RoutedEvent = InputElement.PointerPressedEvent
                };
                holdButton.RaiseEvent(pressed);
                Assert.True(pressed.Handled);
                Assert.Same(holdButton, pointer.Captured);
                var holdField = typeof(MainViewModel).GetField("_emergencyUnlockCts",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Assert.NotNull(holdField.GetValue(viewModel));

                var released = new PointerReleasedEventArgs(holdButton, pointer, window, new Point(1, 1), 1,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                    KeyModifiers.None, MouseButton.Left)
                {
                    RoutedEvent = InputElement.PointerReleasedEvent
                };
                holdButton.RaiseEvent(released);
                Assert.True(released.Handled);
                Assert.Null(pointer.Captured);
                Assert.Null(holdField.GetValue(viewModel));
            }
            finally
            {
                window.Close();
            }

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, true);
            var confirmation = new MainView { DataContext = viewModel };
            var confirmationWindow = new Window { Content = confirmation, Width = 900, Height = 700 };
            try
            {
                confirmationWindow.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(confirmation.FindControl<Button>("StartConfirmationCancelButton"),
                    confirmationWindow.FocusManager?.GetFocusedElement());
                Assert.Equal(720, confirmation.FindControl<Border>("StartDialogCard")!.MaxWidth);
            }
            finally
            {
                confirmationWindow.Close();
            }
        }
    }

    public sealed class TestApplication : Application;

    public sealed class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
