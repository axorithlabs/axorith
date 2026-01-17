using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Module.AppBlocker;

internal sealed class Settings : IDisposable
{
    private static readonly Dictionary<string, string[]> CategoryProcesses = new()
    {
        ["Gaming"] =
        [
            "steam", "steamwebhelper", "epicgameslauncher", "eadesktop", "origin",
            "battle.net", "gog galaxy", "ubisoft connect", "upc", "riotclientservices",
            "leagueclient", "valorant", "dota2", "csgo", "cs2", "minecraft",
            "robloxplayerbeta", "xbox", "xboxapp", "geforce experience"
        ],
        ["Social"] =
        [
            "discord", "telegram", "whatsapp", "messenger", "slack", "skype",
            "zoom", "teams", "signal", "viber", "wechat", "line", "element"
        ],
        ["Browsers"] =
        [
            "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",
            "safari", "chromium", "waterfox", "librewolf", "tor browser"
        ],
        ["Entertainment"] =
        [
            "spotify", "netflix", "vlc", "itunes", "amazon music", "deezer",
            "plex", "kodi", "foobar2000", "aimp", "winamp", "musicbee",
            "potplayer", "mpc-hc", "mpv"
        ],
        ["Productivity"] =
        [
            "notion", "obsidian", "evernote", "onenote", "todoist", "ticktick",
            "trello", "asana", "clickup", "roam research"
        ],
        ["Email"] =
        [
            "outlook", "thunderbird", "mailspring", "em client", "mailbird",
            "spark", "postbox", "nylas mail"
        ],
        ["Development"] =
        [
            "code", "rider64", "idea64", "pycharm64", "webstorm64", "clion64",
            "goland64", "phpstorm64", "datagrip64", "android studio",
            "sublime_text", "atom", "notepad++", "vim", "emacs"
        ],
        ["Design"] =
        [
            "photoshop", "illustrator", "figma", "sketch", "affinity designer",
            "affinity photo", "gimp", "inkscape", "canva", "adobe xd", "blender"
        ],
        ["Office"] =
        [
            "winword", "excel", "powerpnt", "msaccess", "onenote",
            "libreoffice", "soffice", "wps office", "google docs"
        ]
    };

    private readonly Setting<List<string>> _categories;
    private readonly Setting<string> _customProcessList;
    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;

    public Settings()
    {
        _categories = Setting.AsMultiChoice(
            key: "Categories",
            label: "Block Categories",
            defaultValues: ["Gaming", "Social", "Browsers", "Entertainment"],
            initialChoices:
            [
                new KeyValuePair<string, string>("Gaming", "Gaming (Steam, Epic, Battle.net, Riot...)"),
                new KeyValuePair<string, string>("Social", "Social & Messaging (Discord, Telegram, Slack, Zoom...)"),
                new KeyValuePair<string, string>("Browsers", "Web Browsers (Chrome, Firefox, Edge...)"),
                new KeyValuePair<string, string>("Entertainment", "Entertainment (Spotify, Netflix, VLC...)"),
                new KeyValuePair<string, string>("Productivity", "Productivity (Notion, Obsidian, Todoist...)"),
                new KeyValuePair<string, string>("Email", "Email Clients (Outlook, Thunderbird...)"),
                new KeyValuePair<string, string>("Development", "Development Tools (VS Code, JetBrains IDEs...)"),
                new KeyValuePair<string, string>("Design", "Design Tools (Photoshop, Figma, Blender...)"),
                new KeyValuePair<string, string>("Office", "Office Apps (Word, Excel, LibreOffice...)")
            ],
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

    public IReadOnlyList<ISetting> GetSettings() => _allSettings;

    public IReadOnlyList<IAction> GetActions() => _allActions;

    public Task<ValidationResult> ValidateAsync()
    {
        var cats = _categories.GetCurrentValue();
        var custom = _customProcessList.GetCurrentValue();

        if (cats.Count == 0 && string.IsNullOrWhiteSpace(custom))
        {
            return Task.FromResult(ValidationResult.Warn("No categories or apps selected. The module will not block anything."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    public IEnumerable<string> GetProcesses()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in _categories.GetCurrentValue())
        {
            if (CategoryProcesses.TryGetValue(cat, out var procs))
            {
                foreach (var p in procs) result.Add(p);
            }
        }

        var custom = _customProcessList.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            foreach (var p in custom.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) result.Add(trimmed);
            }
        }

        return result;
    }

    public void Dispose()
    {
        _categories.Dispose();
        _customProcessList.Dispose();
    }
}
