using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Axorith.Client;
using Axorith.Client.Services;
using Axorith.Client.ViewModels;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ReactiveUI;
using Xunit;

namespace Axorith.Integrations.Tests;

[Collection("SiteBlocker extension pipes")]
public sealed class ClientSettingsPersistenceTests
{
    [AvaloniaFact]
    public async Task FailedSaveKeepsEditsDirtyAndDoesNotApplyStartupChanges()
    {
        var path = Path.Combine(ApplicationPaths.Config, "clientsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var previous = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        var preferencePath = Path.Combine(ApplicationPaths.Config, "telemetry-preference.txt");
        var preference = File.Exists(preferencePath) ? await File.ReadAllBytesAsync(preferencePath) : null;
        await File.WriteAllTextAsync(path, "{\"MinimizeToTrayOnClose\":true}");
        try
        {
            var config = new Configuration { Ui = new ClientUiConfiguration { MinimizeToTrayOnClose = true } };
            var startup = new Mock<IAutoStartManager>();
            using var services = new ServiceCollection().BuildServiceProvider();
            var shell = new ShellViewModel();
            var previousScreen = shell.Content;
            using var vm = new SettingsViewModel(shell, new UiSettingsStore(NullLogger<UiSettingsStore>.Instance),
                startup.Object, NoopTelemetryService.Instance, Options.Create(config), services, NullLogger<SettingsViewModel>.Instance);
            vm.MinimizeToTrayOnClose = false;
            vm.AutoStartEnabled = true;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ((ReactiveCommand<Unit, Unit>)vm.SaveCommand).Execute();
                Assert.True(vm.HasUnsavedChanges);
                await ((ReactiveCommand<Unit, Unit>)vm.BackCommand).Execute();
                Assert.Same(previousScreen, shell.Content);
                Assert.True(config.Ui.MinimizeToTrayOnClose);
                Assert.Equal("{\"MinimizeToTrayOnClose\":true}", await File.ReadAllTextAsync(path));
                startup.Verify(manager => manager.EnableAutoStart(It.IsAny<bool>()), Times.Never);
                startup.Verify(manager => manager.DisableAutoStart(), Times.Never);
            }
            await ((ReactiveCommand<Unit, Unit>)vm.SaveCommand).Execute();
            Assert.False(vm.HasUnsavedChanges);
            Assert.False(new UiSettingsStore(NullLogger<UiSettingsStore>.Instance).LoadOrDefault().MinimizeToTrayOnClose);
        }
        finally
        {
            if (previous is null) File.Delete(path); else await File.WriteAllBytesAsync(path, previous);
            if (preference is null) File.Delete(preferencePath); else await File.WriteAllBytesAsync(preferencePath, preference);
        }
    }

    [AvaloniaFact]
    public async Task SaveCommandPersistsTrayStartupAndInputSettingsForANewClient()
    {
        var settingsPath = Path.Combine(ApplicationPaths.Config, "clientsettings.json");
        var previous = File.Exists(settingsPath) ? await File.ReadAllBytesAsync(settingsPath) : null;
        var preferencePath = Path.Combine(ApplicationPaths.Config, "telemetry-preference.txt");
        var preference = File.Exists(preferencePath) ? await File.ReadAllBytesAsync(preferencePath) : null;
        try
        {
            var config = new Configuration { Ui = new ClientUiConfiguration
            {
                TelemetryEnabled = false, MinimizeToTrayOnClose = true,
                InputHistory = new() { ["CustomPath"] = ["editor.exe"] },
                SettingsInput = new SettingsInputConfiguration { TextDebounceMs = 900 }
            } };
            var startup = new Mock<IAutoStartManager>();
            startup.Setup(manager => manager.EnableAutoStart(false)).Returns(true);
            using var services = new ServiceCollection().BuildServiceProvider();
            var store = new UiSettingsStore(NullLogger<UiSettingsStore>.Instance);
            using var vm = new SettingsViewModel(new ShellViewModel(), store, startup.Object,
                NoopTelemetryService.Instance, Options.Create(config), services, NullLogger<SettingsViewModel>.Instance);
            vm.AutoStartEnabled = true;
            vm.AutoStartMinimized = false;
            vm.MinimizeToTrayOnClose = false;
            Assert.True(vm.HasUnsavedChanges);

            await ((ReactiveCommand<Unit, Unit>)vm.SaveCommand).Execute();

            var restored = new UiSettingsStore(NullLogger<UiSettingsStore>.Instance).LoadOrDefault();
            Assert.True(restored.AutoStartEnabled);
            Assert.False(restored.AutoStartMinimized);
            Assert.False(restored.MinimizeToTrayOnClose);
            Assert.False(restored.TelemetryEnabled);
            Assert.Equal(900, restored.SettingsInput.TextDebounceMs);
            Assert.Equal(["editor.exe"], restored.InputHistory["CustomPath"]);
            Assert.False(vm.HasUnsavedChanges);
            startup.Verify(manager => manager.EnableAutoStart(false), Times.Once);
        }
        finally
        {
            if (previous is null) File.Delete(settingsPath); else await File.WriteAllBytesAsync(settingsPath, previous);
            if (preference is null) File.Delete(preferencePath); else await File.WriteAllBytesAsync(preferencePath, preference);
        }
    }
}
