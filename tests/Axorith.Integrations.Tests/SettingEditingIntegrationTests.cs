using System.ComponentModel;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Axorith.Client.Adapters;
using Axorith.Client.CoreSdk;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Xunit;

namespace Axorith.Integrations.Tests;

public partial class HostGrpcEndToEndTests
{
    [AvaloniaFact]
    public async Task ClosingEditorBeforeBeginEditArrivesReleasesTheLateSandbox()
    {
        var transport = new DelayedBeginEdit();
        using var http = factory.CreateDefaultClient(transport);
        using var channel = TestGrpc.CreateAuthenticatedChannel(http,
            File.ReadAllText(Path.Combine(factory.TestDataPath, "config", ".auth_token")));
        using var api = new GrpcModulesApi(new ModulesService.ModulesServiceClient(channel),
            Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        var definition = (await api.ListModulesAsync()).Single(module => module.Id == factory.SiteBlockerModuleId);
        var model = new Axorith.Core.Models.ConfiguredModule { ModuleId = definition.Id };
        using var editor = new ConfiguredModuleViewModel(definition, model, api, services);
        try
        {
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            editor.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Volatile.Read(ref transport.Ended) < 1) await Task.Delay(10, timeout.Token);
            transport.Continue.TrySetResult();
            while (Volatile.Read(ref transport.Ended) < 2) await Task.Delay(10, timeout.Token);
            Assert.Null(factory.Services.GetRequiredService<Axorith.Host.Services.DesignTimeSandboxManager>()
                .GetModule(model.InstanceId));
            Assert.Empty(editor.Settings);
            Assert.Empty(editor.Actions);
        }
        finally
        {
            transport.Continue.TrySetResult();
            await api.EndEditAsync(model.InstanceId);
        }
    }

    [AvaloniaFact]
    public async Task SharedTriggerCardsOpenAndRemoveTheCorrectTrigger()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var editor = new SessionEditorViewModel(null!, null!, null!, null!, null!, services);
        await editor.InitializationTask;
        using (editor.AddScheduleTriggerCommand.Execute().Subscribe())
        using (editor.AddStopAfterDurationTriggerCommand.Execute().Subscribe())
            Dispatcher.UIThread.RunJobs();
        var start = Assert.Single(editor.Triggers);
        var stop = Assert.Single(editor.StopTriggers);
        var view = new SessionEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 800, Height = 1200 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cards = view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("ItemCard")).ToArray();
            var startCard = Assert.Single(cards, card => ReferenceEquals(card.DataContext, start));
            var stopCard = Assert.Single(cards, card => ReferenceEquals(card.DataContext, stop));
            Assert.NotNull(startCard.Command);
            Assert.NotNull(stopCard.Command);
            stopCard.Command.Execute(stopCard.CommandParameter);
            Assert.Same(stop, editor.SelectedStopTrigger);
            startCard.Command.Execute(startCard.CommandParameter);
            Assert.Same(start, editor.SelectedTrigger);
            foreach (var card in new[] { startCard, stopCard })
            {
                var remove = Assert.Single(card.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("delete-btn"));
                Assert.NotNull(remove.Command);
                remove.Command.Execute(remove.CommandParameter);
            }
            Assert.Empty(editor.Triggers);
            Assert.Empty(editor.StopTriggers);
            Assert.Null(editor.SelectedTrigger);
            Assert.Null(editor.SelectedStopTrigger);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FocusLossSendsOneSettingChangeAndRetryPreservesItsValue(bool failFirstRequest)
    {
        var transport = new SettingRequestCounter(failFirstRequest);
        using var http = factory.CreateDefaultClient(transport);
        using var channel = TestGrpc.CreateAuthenticatedChannel(http,
            File.ReadAllText(Path.Combine(factory.TestDataPath, "config", ".auth_token")));
        using var api = new GrpcModulesApi(new ModulesService.ModulesServiceClient(channel),
            Policy.Handle<Exception>().RetryAsync(1), NullLogger.Instance);
        var instanceId = Guid.NewGuid();
        var edit = await api.BeginEditAsync(factory.SiteBlockerModuleId, instanceId,
            new Dictionary<string, object?>());
        using var adapter = new ModuleSettingAdapter(edit.SettingsInfo.Settings.Single(setting => setting.Key == "CustomSites"));
        using var services = new ServiceCollection().AddSingleton(Options.Create(new Axorith.Client.Configuration
        {
            Ui = new Axorith.Client.ClientUiConfiguration
            {
                SettingsInput = new Axorith.Client.SettingsInputConfiguration { TextDebounceMs = 250, FlushOnFocusLoss = true }
            }
        })).BuildServiceProvider();
        using var setting = new SettingViewModel(adapter, factory.SiteBlockerModuleId, instanceId, api, services);
        try
        {
            setting.OnFocusGained();
            setting.StringValue = "editing.example";
            setting.OnFocusLost();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (transport.Completed == 0) await Task.Delay(10, timeout.Token);
            await Task.Delay(400, timeout.Token);
            Assert.Equal(failFirstRequest ? 2 : 1, transport.Attempts);
            var current = await new ModulesService.ModulesServiceClient(channel).BeginEditAsync(new BeginEditRequest
            {
                ModuleId = factory.SiteBlockerModuleId.ToString(), ModuleInstanceId = instanceId.ToString()
            });
            Assert.Equal("editing.example", current.Settings.Single(value => value.Key == "CustomSites").StringValue);

            setting.Dispose();
            var notifications = 0;
            ((INotifyPropertyChanged)setting).PropertyChanged += (_, _) => notifications++;
            adapter.SetValueFromString("remote-after-close.example");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, notifications);
        }
        finally
        {
            await api.EndEditAsync(instanceId);
        }
    }

    private sealed class DelayedBeginEdit : DelegatingHandler
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Ended;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/BeginEdit", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (path.EndsWith("/EndEdit", StringComparison.Ordinal)) Interlocked.Increment(ref Ended);
            return response;
        }
    }

    private sealed class SettingRequestCounter(bool failFirstRequest) : DelegatingHandler
    {
        public int Attempts;
        public int Completed;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var settingUpdate = request.RequestUri!.AbsolutePath.EndsWith("/UpdateSetting", StringComparison.Ordinal);
            if (settingUpdate && Interlocked.Increment(ref Attempts) == 1 && failFirstRequest)
                throw new HttpRequestException("Transient transport failure");
            var response = await base.SendAsync(request, cancellationToken);
            if (settingUpdate) Interlocked.Increment(ref Completed);
            return response;
        }
    }
}
