using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Module.AppBlocker;

internal sealed class Settings
{
    private readonly Setting<List<string>> _categories;
    private readonly Setting<string> _manualProcessList;
    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;

    private static readonly Dictionary<string, List<string>> CategoryProcesses = new()
    {
        ["Games"] = ["steam", "epicgameslauncher", "battle.net", "dota2", "csgo", "valorant", "league of legends"],
        ["Social"] = ["discord", "telegram", "slack", "skype", "whatsapp"],
        ["Browsers"] = ["chrome", "firefox", "msedge", "opera", "brave"]
    };

    public Settings()
    {
        _categories = Setting.AsMultiChoice(
            key: "Categories",
            label: "Block Categories",
            defaultValues: [],
            initialChoices:
            [
                new KeyValuePair<string, string>("Games", "Block Games (Steam, Epic, etc.)"),
                new KeyValuePair<string, string>("Social", "Block Communication (Discord, Telegram)"),
                new KeyValuePair<string, string>("Browsers", "Block Browsers")
            ],
            description: "Select categories to automatically block common apps."
        );

        _manualProcessList = Setting.AsTextArea(
            key: "ManualProcessList",
            label: "Manual Blocklist",
            defaultValue: "",
            description: "Manually add process names (e.g. 'notepad', 'calc'). Separate by comma or new line."
        );

        _allSettings = [_categories, _manualProcessList];
        _allActions = [];
    }

    public IReadOnlyList<ISetting> GetSettings() => _allSettings;
    public IReadOnlyList<IAction> GetActions() => _allActions;

    public Task<ValidationResult> ValidateAsync()
    {
        var cats = _categories.GetCurrentValue();
        var manual = _manualProcessList.GetCurrentValue();

        if (cats.Count == 0 && string.IsNullOrWhiteSpace(manual))
        {
            return Task.FromResult(ValidationResult.Warn("No categories or processes selected. The module will not block anything."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    public IEnumerable<string> GetProcesses()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var selectedCats = _categories.GetCurrentValue();
        foreach (var cat in selectedCats)
        {
            if (!CategoryProcesses.TryGetValue(cat, out var procs))
            {
                continue;
            }

            foreach (var p in procs) result.Add(p);
        }

        var manual = _manualProcessList.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(manual))
        {
            return result;
        }

        {
            var manualList = manual.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            
            foreach (var p in manualList) result.Add(p);
        }

        return result;
    }
}