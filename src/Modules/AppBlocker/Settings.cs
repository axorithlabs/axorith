using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.AppBlocker;

internal sealed class Settings
{
    public Setting<string> ProcessList { get; }
    
    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;

    public Settings()
    {
        ProcessList = Setting.AsTextArea(
            key: "ProcessList",
            label: "Processes to Block",
            defaultValue: "discord, steam, telegram",
            description: "List of process names to block (e.g. 'discord', 'steam'). Separate by comma or new line."
        );

        _allSettings = [ProcessList];
        _allActions = [];
    }

    public IReadOnlyList<ISetting> GetSettings() => _allSettings;
    public IReadOnlyList<IAction> GetActions() => _allActions;

    public Task<ValidationResult> ValidateAsync()
    {
        var list = ProcessList.GetCurrentValue();
        return Task.FromResult(string.IsNullOrWhiteSpace(list) ? ValidationResult.Warn("Process list is empty. The module will not block anything.") : ValidationResult.Success);
    }

    public IEnumerable<string> GetProcesses()
    {
        var raw = ProcessList.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return raw.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }
}