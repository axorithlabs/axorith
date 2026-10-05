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
    private readonly IReadOnlyDictionary<string, LauncherAppBase> _modules;
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
    public Setting<string> LegacyLauncherModuleKey { get; }
    public Setting<bool> UseCustomWorkingDirectory { get; }
    public Setting<string> WorkingDirectory { get; }

    public Settings(IAppDiscoveryService appDiscovery, IReadOnlyDictionary<string, LauncherAppBase> modules)
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
        LegacyLauncherModuleKey = Setting.AsText(ApplicationSelector.LegacyModuleKeySetting,
            "Legacy Launcher Type", "", isVisible: false);
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
                var proxy = new Action($"{moduleKey}.{action.Key}", action.GetCurrentLabel(),
                    action.GetCurrentEnabled() && _selectedModule == moduleKey, action.SettingKey);
                proxy.OnInvokeAsync(action.InvokeAsync);
                _subscriptions.Add(action.Label.Subscribe(proxy.SetLabel));
                _subscriptions.Add(action.IsEnabled.Subscribe(enabled =>
                    proxy.SetEnabled(enabled && _selectedModule == moduleKey)));
                _moduleActions.Add(proxy);
            }
        }

        ApplicationPath.Value.Subscribe(UpdateModuleVisibility);
        _subscriptions.Add(LegacyLauncherModuleKey.Value.Subscribe(_ =>
            UpdateModuleVisibility(ApplicationPath.GetCurrentValue())));
        ProcessMode.Value.Subscribe(_ => UpdateOwnVisibility());
        SetupBaseReactiveVisibility();
        _subscriptions.Add(WindowState.Value.Subscribe(_ => UpdateOwnVisibility()));
        _subscriptions.Add(UseCustomSize.Value.Subscribe(_ => UpdateOwnVisibility()));
        _subscriptions.Add(MoveToMonitor.Value.Subscribe(_ => UpdateOwnVisibility()));
        _subscriptions.Add(UseCustomWorkingDirectory.Value.Subscribe(_ => UpdateOwnVisibility()));
        UpdateModuleVisibility(ApplicationPath.GetCurrentValue());
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings() =>
        [CustomPath, ApplicationArgs, ProjectPath, LegacyLauncherModuleKey, UseCustomWorkingDirectory, WorkingDirectory,
            .. _moduleSettings.Select(entry => entry.Setting)];

    protected override IEnumerable<IAction> GetAdditionalActions() => _moduleActions;

    protected override Task InitializeAdditionalAsync()
    {
        var choices = ApplicationSelector.GetInstalledChoices(
            _appDiscovery, app => app.ExecutablePath, ApplicationSelector.IsSupportedLauncherApp);
        choices.Add(new(CustomApp, "Custom App"));
        ApplicationPath.SetChoices(choices);
        EnsureApplicationPathChoice(ApplicationPath.GetCurrentValue());
        return Task.CompletedTask;
    }

    public Task EnsureModuleInitializedAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleKey, out var module))
            return Task.CompletedTask;

        return _moduleInitialization.GetOrAdd(moduleKey, _ => module.InitializeAsync(cancellationToken));
    }

    public string? GetSelectedModuleKey() =>
        ApplicationSelector.GetLauncherModuleKey(ApplicationPath.GetCurrentValue(),
            LegacyLauncherModuleKey.GetCurrentValue());

    protected override async Task<ValidationResult> ValidateAdditionalAsync()
    {
        if (ApplicationPath.GetCurrentValue() == CustomApp &&
            (string.IsNullOrWhiteSpace(CustomPath.GetCurrentValue()) || !File.Exists(CustomPath.GetCurrentValue())))
        {
            return ValidationResult.Fail(
                new Dictionary<string, string> { [CustomPath.Key] = "Select an existing application executable." },
                "Configuration contains errors.");
        }

        var moduleKey = GetSelectedModuleKey();
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
        EnsureApplicationPathChoice(selectedPath);
        _selectedModule = ApplicationSelector.GetLauncherModuleKey(selectedPath,
            LegacyLauncherModuleKey.GetCurrentValue());
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

    private void EnsureApplicationPathChoice(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || selectedPath == CustomApp)
            return;

        var choices = ((ISetting)ApplicationPath).GetCurrentChoices();
        if (choices == null || choices.Any(choice => choice.Key == selectedPath))
            return;

        var fileName = Path.GetFileName(selectedPath);
        var label = string.IsNullOrWhiteSpace(fileName) ? selectedPath : $"{fileName} (Custom)";
        ApplicationPath.SetChoices([new(selectedPath, label), .. choices]);
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
        base.Dispose();
    }
}
