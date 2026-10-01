using System.Collections.Concurrent;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.ApplicationLauncher;

internal sealed class Settings : LauncherSettingsBase
{
    public const string CustomApp = "custom-app";

    private static readonly HashSet<string> SharedSettings = new(StringComparer.Ordinal)
    {
        "ProcessMode", "WindowState", "UseCustomSize", "WindowWidth", "WindowHeight",
        "MoveToMonitor", "TargetMonitor", "LifecycleMode", "BringToForeground",
        "ApplicationArgs", "ProjectPath"
    };

    private readonly IAppDiscoveryService _appDiscovery;
    private readonly IReadOnlyDictionary<string, ILauncherApp> _modules;
    private readonly ConcurrentDictionary<string, Task> _moduleInitialization = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISetting> _modulePaths = new(StringComparer.Ordinal);
    private readonly List<(string Module, SelectedAppSetting Setting)> _moduleSettings = [];
    private readonly List<Action> _moduleActions = [];
    private readonly List<IDisposable> _subscriptions = [];
    private string? _selectedModule;

    public override Setting<string> ApplicationPath { get; }
    public Setting<string> CustomPath { get; }
    public Setting<string> ApplicationArgs { get; }
    public Setting<string> ProjectPath { get; }
    public Setting<bool> UseCustomWorkingDirectory { get; }
    public Setting<string> WorkingDirectory { get; }

    public Settings(IAppDiscoveryService appDiscovery, IReadOnlyDictionary<string, ILauncherApp> modules)
    {
        _appDiscovery = appDiscovery;
        _modules = modules;

        ApplicationPath = Setting.AsChoice("ApplicationPath", "Application", string.Empty,
            [new KeyValuePair<string, string>(CustomApp, "Custom App")],
            "");
        CustomPath = Setting.AsFilePicker("CustomPath", "Application Path", "",
            filter: "Executable files (*.exe)|*.exe|All files (*.*)|*.*", isVisible: false);
        ApplicationArgs = Setting.AsText("ApplicationArgs", "Launch Arguments", "", isVisible: false);
        ProjectPath = Setting.AsDirectoryPicker("ProjectPath", "Project Folder", "", isVisible: false);
        UseCustomWorkingDirectory = Setting.AsCheckbox("UseCustomWorkingDirectory", "Use Custom Working Directory", false);
        WorkingDirectory = Setting.AsDirectoryPicker("WorkingDirectory", "Working Directory",
            Environment.CurrentDirectory, isVisible: false);

        foreach (var (moduleKey, module) in modules)
        {
            var moduleSettings = module.GetSettings();
            if (moduleSettings.Count > 0)
                _modulePaths[moduleKey] = moduleSettings[0];

            foreach (var setting in moduleSettings.Skip(1))
            {
                if (SharedSettings.Contains(setting.Key) || setting.Key.EndsWith("Path", StringComparison.Ordinal))
                    continue;
                if (_moduleSettings.Any(entry => entry.Setting.Key == setting.Key))
                    continue;

                _moduleSettings.Add((moduleKey, new SelectedAppSetting(setting)));
            }

            foreach (var action in module.GetActions())
            {
                var proxy = Action.Create($"{moduleKey}.{action.Key}", action.GetCurrentLabel(),
                    action.GetCurrentEnabled() && _selectedModule == moduleKey, action.SettingKey);
                proxy.OnInvokeAsync(action.InvokeAsync);
                _subscriptions.Add(action.Label.Subscribe(proxy.SetLabel));
                _subscriptions.Add(action.IsEnabled.Subscribe(enabled =>
                    proxy.SetEnabled(enabled && _selectedModule == moduleKey)));
                _moduleActions.Add(proxy);
            }
        }

        ApplicationPath.Value.Subscribe(UpdateModuleVisibility);
        ProcessMode.Value.Subscribe(_ => UpdateOwnVisibility());
        SetupBaseReactiveVisibility();
        _subscriptions.Add(WindowState.Value.Subscribe(_ => UpdateOwnVisibility()));
        _subscriptions.Add(UseCustomSize.Value.Subscribe(_ => UpdateOwnVisibility()));
        _subscriptions.Add(MoveToMonitor.Value.Subscribe(_ => UpdateOwnVisibility()));
        _subscriptions.Add(UseCustomWorkingDirectory.Value.Subscribe(_ => UpdateOwnVisibility()));
        UpdateModuleVisibility(ApplicationPath.GetCurrentValue());
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield return CustomPath;
        yield return ApplicationArgs;
        yield return ProjectPath;
        yield return UseCustomWorkingDirectory;
        yield return WorkingDirectory;
        foreach (var (_, setting) in _moduleSettings)
            yield return setting;
    }

    protected override IEnumerable<IAction> GetAdditionalActions() => _moduleActions;

    protected override async Task InitializeAdditionalAsync()
    {
        var choices = ApplicationSelector.GetInstalledChoices(
            _appDiscovery,
            app => app.ExecutablePath,
            ApplicationSelector.IsSupportedLauncherApp);
        choices.Add(new KeyValuePair<string, string>(CustomApp, "Custom App"));
        ApplicationPath.SetChoices(choices);
    }

    public Task EnsureModuleInitializedAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleKey, out var module))
            return Task.CompletedTask;

        return _moduleInitialization.GetOrAdd(moduleKey, _ => module.InitializeAsync(cancellationToken));
    }

    protected override async Task<ValidationResult> ValidateAdditionalAsync()
    {
        if (ApplicationPath.GetCurrentValue() == CustomApp &&
            (string.IsNullOrWhiteSpace(CustomPath.GetCurrentValue()) || !File.Exists(CustomPath.GetCurrentValue())))
        {
            return ValidationResult.Fail(
                new Dictionary<string, string> { [CustomPath.Key] = "Select an existing application executable." },
                "Configuration contains errors.");
        }

        var moduleKey = ApplicationSelector.GetLauncherModuleKey(ApplicationPath.GetCurrentValue());
        if (moduleKey == null || !_modules.TryGetValue(moduleKey, out var module))
            return ValidationResult.Success;

        await EnsureModuleInitializedAsync(moduleKey).ConfigureAwait(false);
        SynchronizeModule(moduleKey);
        return await module.ValidateSettingsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public void SynchronizeModule(string moduleKey)
    {
        if (!_modules.TryGetValue(moduleKey, out var module))
            return;

        if (_modulePaths.TryGetValue(moduleKey, out var pathSetting))
            pathSetting.SetValueFromObject(ApplicationPath.GetCurrentValue());

        var childSettings = module.GetSettings().ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        foreach (var setting in GetAllSettings())
            if (SharedSettings.Contains(setting.Key) && childSettings.TryGetValue(setting.Key, out var childSetting))
                childSetting.SetValueFromObject(setting.GetCurrentValueAsObject());
    }

    private void UpdateModuleVisibility(string selectedPath)
    {
        _selectedModule = ApplicationSelector.GetLauncherModuleKey(selectedPath);
        SynchronizeModule(_selectedModule ?? string.Empty);
        if (_selectedModule != null)
            _ = EnsureModuleInitializedAsync(_selectedModule);

        foreach (var (moduleKey, setting) in _moduleSettings)
            setting.SetSelected(moduleKey == _selectedModule);

        foreach (var module in _modules)
            foreach (var action in module.Value.GetActions())
            {
                var proxy = _moduleActions.FirstOrDefault(item => item.Key == $"{module.Key}.{action.Key}");
                proxy?.SetEnabled(module.Key == _selectedModule && action.GetCurrentEnabled());
            }

        UpdateOwnVisibility();
    }

    private void UpdateOwnVisibility()
    {
        var application = ApplicationPath.GetCurrentValue();
        var hasApplication = !string.IsNullOrWhiteSpace(application);
        var custom = application == CustomApp;
        var isIde = _selectedModule is "VSCode" or "JetBrainsIDE";
        var acceptsArguments = ProcessMode.GetCurrentValue() is "LaunchNew" or "LaunchOrAttach";

        ProcessMode.SetVisibility(hasApplication);
        WindowState.SetVisibility(hasApplication);
        UseCustomSize.SetVisibility(hasApplication && WindowState.GetCurrentValue() == "Normal");
        WindowWidth.SetVisibility(hasApplication && WindowState.GetCurrentValue() == "Normal" && UseCustomSize.GetCurrentValue());
        WindowHeight.SetVisibility(hasApplication && WindowState.GetCurrentValue() == "Normal" && UseCustomSize.GetCurrentValue());
        MoveToMonitor.SetVisibility(hasApplication);
        TargetMonitor.SetVisibility(hasApplication && MoveToMonitor.GetCurrentValue());
        LifecycleMode.SetVisibility(hasApplication);
        BringToForeground.SetVisibility(hasApplication && WindowState.GetCurrentValue() != "Minimized");
        UseCustomWorkingDirectory.SetVisibility(hasApplication);
        WorkingDirectory.SetVisibility(hasApplication && UseCustomWorkingDirectory.GetCurrentValue());
        CustomPath.SetVisibility(custom);
        ApplicationArgs.SetVisibility(hasApplication && acceptsArguments && (custom || isIde));
        ProjectPath.SetVisibility(hasApplication && isIde);
    }

    public override void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        foreach (var (_, setting) in _moduleSettings)
            setting.Dispose();
        foreach (var action in _moduleActions)
            action.Dispose();

        CustomPath.Dispose();
        ApplicationArgs.Dispose();
        ProjectPath.Dispose();
        UseCustomWorkingDirectory.Dispose();
        WorkingDirectory.Dispose();
        base.Dispose();
    }
}
