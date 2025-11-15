using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Sdk.Tests.Helpers;

/// <summary>
///     Builder class for creating test modules with fluent API
/// </summary>
public class TestModuleBuilder
{
    private readonly List<ISetting> _settings = [];
    private readonly List<IAction> _actions = [];
    private Func<CancellationToken, Task>? _onInitialize;
    private Func<CancellationToken, Task>? _onSessionStart;
    private Func<Task>? _onSessionEnd;
    private ValidationResult _validationResult = ValidationResult.Success;

    public TestModuleBuilder WithSetting(ISetting setting)
    {
        _settings.Add(setting);
        return this;
    }

    public TestModuleBuilder WithAction(IAction action)
    {
        _actions.Add(action);
        return this;
    }

    public TestModuleBuilder WithInitialize(Func<CancellationToken, Task> onInitialize)
    {
        _onInitialize = onInitialize;
        return this;
    }

    public TestModuleBuilder WithSessionStart(Func<CancellationToken, Task> onSessionStart)
    {
        _onSessionStart = onSessionStart;
        return this;
    }

    public TestModuleBuilder WithSessionEnd(Func<Task> onSessionEnd)
    {
        _onSessionEnd = onSessionEnd;
        return this;
    }

    public TestModuleBuilder WithValidationResult(ValidationResult result)
    {
        _validationResult = result;
        return this;
    }

    public TestModule Build()
    {
        return new TestModule(
            _settings,
            _actions,
            _onInitialize,
            _onSessionStart,
            _onSessionEnd,
            _validationResult);
    }
}

public class TestModule(
    List<ISetting> settings,
    List<IAction> actions,
    Func<CancellationToken, Task>? onInitialize,
    Func<CancellationToken, Task>? onSessionStart,
    Func<Task>? onSessionEnd,
    ValidationResult validationResult)
    : IModule
{
    public IReadOnlyList<ISetting> GetSettings()
    {
        return settings;
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return actions;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return onInitialize?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public object GetSettingsViewModel()
    {
        throw new NotImplementedException();
    }

    public Task OnSessionStartAsync(CancellationToken cancellationToken = default)
    {
        return onSessionStart?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        return onSessionEnd?.Invoke() ?? Task.CompletedTask;
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(validationResult);
    }

    public Type? CustomSettingsViewType { get; }

    public void Dispose()
    {
        // No-op for test module
    }
}