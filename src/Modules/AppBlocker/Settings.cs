using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Module.AppBlocker;

internal sealed class Settings : IDisposable
{
    private static Dictionary<string, string[]>? _categoryProcesses;
    private static readonly Lock _loadLock = new();

    private readonly Setting<List<string>> _categories;
    private readonly Setting<string> _customProcessList;
    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;

    public Settings()
    {
        EnsureCategoriesLoaded();

        _categories = Setting.AsMultiChoice(
            key: "Categories",
            label: "Block Categories",
            defaultValues: ["Gaming", "Social", "Browsers", "Entertainment"],
            initialChoices: BuildCategoryChoices(),
            description: "Select categories to block. Apps from selected categories will be automatically terminated."
        );

        _customProcessList = Setting.AsTextArea(
            key: "CustomProcessList",
            label: "Custom Apps",
            defaultValue: "",
            description: "Additional process names to block (comma or newline separated). Example: notepad, calc"
        );

        _allSettings = [_categories, _customProcessList];
        _allActions = [];
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _allSettings;
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _allActions;
    }

    public Task<ValidationResult> ValidateAsync()
    {
        var cats = _categories.GetCurrentValue();
        var custom = _customProcessList.GetCurrentValue();

        if (cats.Count == 0 && string.IsNullOrWhiteSpace(custom))
        {
            return Task.FromResult(
                ValidationResult.Warn("No categories or apps selected. The module will not block anything."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    public IEnumerable<string> GetProcesses()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in _categories.GetCurrentValue())
        {
            if (_categoryProcesses!.TryGetValue(cat, out var procs))
            {
                foreach (var p in procs)
                {
                    result.Add(p);
                }
            }
        }

        var custom = _customProcessList.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            foreach (var p in custom.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    private static void EnsureCategoriesLoaded()
    {
        if (_categoryProcesses != null)
        {
            return;
        }

        lock (_loadLock)
        {
            if (_categoryProcesses != null)
            {
                return;
            }

            var jsonPath = Path.Combine(AppContext.BaseDirectory, "Modules", "AppBlocker", "Data", "blocked_apps.json");

            if (!File.Exists(jsonPath))
            {
                _categoryProcesses = new Dictionary<string, string[]>();
                return;
            }

            try
            {
                var json = File.ReadAllText(jsonPath);
                _categoryProcesses = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
                                     ?? new Dictionary<string, string[]>();
            }
            catch
            {
                _categoryProcesses = new Dictionary<string, string[]>();
            }
        }
    }

    private static List<KeyValuePair<string, string>> BuildCategoryChoices()
    {
        var choices = new List<KeyValuePair<string, string>>();

        if (_categoryProcesses == null)
        {
            return choices;
        }

        var descriptions = new Dictionary<string, string>
        {
            ["Gaming"] = "Gaming (Steam, Epic, Battle.net, Riot...)",
            ["Social"] = "Social & Messaging (Discord, Telegram, Slack, Zoom...)",
            ["Browsers"] = "Web Browsers (Chrome, Firefox, Edge...)",
            ["Entertainment"] = "Entertainment (Spotify, Netflix, VLC...)",
            ["Productivity"] = "Productivity (Notion, Obsidian, Todoist...)",
            ["Email"] = "Email Clients (Outlook, Thunderbird...)",
            ["Development"] = "Development Tools (VS Code, JetBrains IDEs...)",
            ["Design"] = "Design Tools (Photoshop, Figma, Blender...)",
            ["Office"] = "Office Apps (Word, Excel, LibreOffice...)"
        };

        foreach (var category in _categoryProcesses.Keys)
        {
            var description = descriptions.TryGetValue(category, out var desc) ? desc : category;
            choices.Add(new KeyValuePair<string, string>(category, description));
        }

        return choices;
    }

    public void Dispose()
    {
        _categories.Dispose();
        _customProcessList.Dispose();
    }
}