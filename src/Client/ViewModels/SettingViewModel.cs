using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services;
using Axorith.Client.Services.Abstractions;
using Axorith.Sdk.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Axorith.Client.ViewModels;

public class SettingViewModel : ReactiveObject, IDisposable
{
    private readonly Guid _moduleInstanceId;
    private readonly Guid _moduleId;
    private readonly IModulesApi _modulesApi;
    private readonly ILogger<SettingViewModel>? _logger;
    private readonly IClientUiSettingsStore? _uiSettingsStore;
    private readonly ClientUiConfiguration? _uiConfig;
    private readonly FilePickerService? _filePickerService;
    private readonly SettingsInputConfiguration _inputConfig;

    private const int ChoiceThrottleMs = 50;

    private bool _isUserEditing;
    private IReadOnlyList<KeyValuePair<string, string>> _rawChoices = [];

    private Timer? _stringDebounceTimer;
    private Timer? _numberThrottleTimer;
    private string? _pendingStringValue;
    private object? _pendingNumberValue;

    public ISetting Setting { get; }

    [Reactive]
    public string Label { get; private set; } = string.Empty;

    [Reactive]
    public bool IsVisible { get; private set; } = true;

    private string _searchText = string.Empty;
    private string _applicationInputText = string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            this.RaiseAndSetIfChanged(ref _searchText, value);
            UpdateDisplayedChoices();
        }
    }

    public bool IsApplicationSelector => Setting.Key is "ApplicationPath" or "AppToAdd";
    public bool IsCustomAppsField => Setting.Key == "CustomProcessList";
    public string SelectorItemActionLabel => Setting.Key == "AppToAdd" ? "Add" : "Select";
    public bool ShowPopupSearch => Setting.Key == "AppToAdd";
    [Reactive]
    public SettingViewModel? ApplicationPicker { get; internal set; }

    [Reactive]
    public ActionViewModel? InlineAction { get; set; }

    public string ApplicationInputText
    {
        get => _applicationInputText;
        set
        {
            if (_applicationInputText == value)
                return;

            this.RaiseAndSetIfChanged(ref _applicationInputText, value);
            SearchText = value;
            IsSelectorOpen = true;
            if (Setting.Key == "ApplicationPath" && !string.IsNullOrEmpty(StringValue))
                StringValue = string.Empty;
        }
    }

    [Reactive]
    public bool IsSelectorOpen { get; set; }


    [Reactive]
    public bool IsReadOnly { get; private set; }

    [Reactive]
    public string? Error { get; set; }

    public event EventHandler? ValueChanged;

    public ObservableCollection<string> History { get; } = [];

    private string? _selectedHistoryItem;

    public string? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            if (_selectedHistoryItem == value) return;
            this.RaiseAndSetIfChanged(ref _selectedHistoryItem, value);
            if (!string.IsNullOrEmpty(value)) StringValue = value;
        }
    }

    public string StringValue
    {
        get => Setting.GetCurrentValueAsObject() as string ?? string.Empty;
        set
        {
            var current = Setting.GetCurrentValueAsObject() as string;
            if (string.Equals(current, value, StringComparison.Ordinal))
            {
                return;
            }

            if (IsTextBasedSetting())
            {
                _isUserEditing = true;
            }

            Setting.SetValueFromString(value);
            this.RaisePropertyChanged();

            HandleStringUpdate(value);
            ValueChanged?.Invoke(this, EventArgs.Empty);
            TryAddToHistory(value);

            UpdateDisplayedChoices();
        }
    }

    public bool BoolValue
    {
        get => Setting.GetCurrentValueAsObject() as bool? ?? false;
        set
        {
            var current = Setting.GetCurrentValueAsObject() as bool?;
            if (current == value)
            {
                return;
            }

            Setting.SetValueFromObject(value);
            this.RaisePropertyChanged();

            _ = SendSettingUpdateAsync(value);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public decimal DecimalValue
    {
        get
        {
            var value = Setting.GetCurrentValueAsObject();
            if (value == null)
            {
                return 0;
            }

            return value switch
            {
                decimal d => d,
                int i => i,
                double db => (decimal)db,
                TimeSpan ts => (decimal)ts.TotalSeconds,
                IConvertible c => Convert.ToDecimal(c),
                _ => 0
            };
        }
        set
        {
            object boxedValue = Setting.ValueType == typeof(int)
                ? (int)Math.Round(value)
                : value;

            var current = Setting.GetCurrentValueAsObject();
            if (current != null && current.Equals(boxedValue))
            {
                return;
            }

            Setting.SetValueFromObject(boxedValue);
            this.RaisePropertyChanged();

            HandleNumberUpdate(boxedValue);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<KeyValuePair<string, string>> DisplayedChoices { get; } = [];
    public ObservableCollection<ApplicationChoiceViewModel> ApplicationChoices { get; } = [];

    public ObservableCollection<MultiChoiceItemViewModel> MultiChoices { get; } = [];
    public ICommand SelectChoiceCommand { get; }
    public ICommand OpenApplicationSelectorCommand { get; }

    public decimal NumberIncrement => Setting.ValueType == typeof(int) ? 1 : 0.1m;

    public string NumberFormatString => Setting.ValueType == typeof(int) ? "0" : "0.##";

    public KeyValuePair<string, string>? SelectedChoice
    {
        get
        {
            var currentValue = StringValue;
            return DisplayedChoices.FirstOrDefault(c => c.Key == currentValue);
        }
        set
        {
            if (!value.HasValue)
            {
                return;
            }

            if (value.Value.Key == StringValue)
            {
                return;
            }

            StringValue = value.Value.Key;
            if (IsApplicationSelector)
                SearchText = string.Empty;
            this.RaisePropertyChanged(nameof(StringValue));
            this.RaisePropertyChanged();
        }
    }

    public ICommand ClickCommand { get; }
    public ICommand RemoveHistoryItemCommand { get; }
    public ICommand BrowseCommand { get; }

    public SettingViewModel(
        ISetting setting,
        Guid moduleId,
        Guid moduleInstanceId,
        IModulesApi modulesApi,
        IServiceProvider? serviceProvider = null)
    {
        Setting = setting;
        _moduleId = moduleId;
        _moduleInstanceId = moduleInstanceId;
        _modulesApi = modulesApi;
        _logger = serviceProvider?.GetService<ILogger<SettingViewModel>>();

        _inputConfig = serviceProvider?.GetService<IOptions<Configuration>>()?.Value.Ui.SettingsInput
                       ?? new SettingsInputConfiguration();

        if (serviceProvider != null)
        {
            _uiSettingsStore = serviceProvider.GetService<IClientUiSettingsStore>();
            _filePickerService = serviceProvider.GetService<FilePickerService>();
            if (_uiSettingsStore != null)
            {
                _uiConfig = _uiSettingsStore.LoadOrDefault();
                LoadHistory();
            }
        }

        ClickCommand = ReactiveCommand.Create(() => BoolValue = true);
        SelectChoiceCommand = ReactiveCommand.CreateFromTask<KeyValuePair<string, string>>(SelectChoiceAsync);
        OpenApplicationSelectorCommand = ReactiveCommand.Create(OpenApplicationSelector);
        RemoveHistoryItemCommand = ReactiveCommand.Create<string>(RemoveHistoryItem);
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);

        Setting.Label.Subscribe(value => RunOnUiThread(() => Label = value));
        Setting.IsVisible.Subscribe(value => RunOnUiThread(() => IsVisible = value));
        Setting.IsReadOnly.Subscribe(value => RunOnUiThread(() => IsReadOnly = value));
        Setting.ValueAsObject.Subscribe(_ =>
        {
            if (_isUserEditing && IsTextBasedSetting()) return;
            RunOnUiThread(() =>
            {
                this.RaisePropertyChanged(nameof(StringValue));
                this.RaisePropertyChanged(nameof(BoolValue));
                this.RaisePropertyChanged(nameof(DecimalValue));
                UpdateDisplayedChoices();
                UpdateMultiChoices();
                RefreshApplicationInputText();
            });
        });

        if (setting.GetCurrentChoices() is { } initialChoices)
        {
            _rawChoices = initialChoices;
        }

        _applicationInputText = GetSelectedApplicationName();

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateDisplayedChoices();
            UpdateMultiChoices();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateDisplayedChoices();
                UpdateMultiChoices();
                RefreshApplicationInputText();
            });
        }

        Setting.Choices?.Subscribe(choices =>
        {
            _rawChoices = choices;
            RunOnUiThread(() =>
            {
                UpdateDisplayedChoices();
                UpdateMultiChoices();
                RefreshApplicationInputText();
            });
        });
    }

    private async Task SelectChoiceAsync(KeyValuePair<string, string> choice)
    {
        SelectedChoice = choice;
        if (Setting.Key != "AppToAdd")
        {
            if (IsApplicationSelector)
            {
                SetApplicationInputText(GetChoiceName(choice.Value));
                IsSelectorOpen = false;
                SearchText = string.Empty;
            }
            return;
        }

        var update = await _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, choice.Key);
        if (!update.Success)
        {
            Error = update.Message;
            return;
        }

        var result = await _modulesApi.InvokeDesignTimeActionAsync(_moduleId, _moduleInstanceId, "AddApp");
        if (!result.Success)
        {
            Error = result.Message;
            return;
        }

        var reset = await _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, string.Empty);
        if (!reset.Success)
            Error = reset.Message;
        Setting.SetValueFromString(string.Empty);
        SetApplicationInputText(string.Empty);
        SearchText = string.Empty;
        IsSelectorOpen = false;
    }

    private void OpenApplicationSelector()
    {
        if (IsSelectorOpen)
        {
            IsSelectorOpen = false;
            return;
        }

        var selectedName = GetSelectedApplicationName();
        SearchText = string.Equals(ApplicationInputText, selectedName, StringComparison.Ordinal)
            ? string.Empty
            : ApplicationInputText;
        IsSelectorOpen = true;
    }

    private string GetSelectedApplicationName()
    {
        var selected = _rawChoices.FirstOrDefault(choice => choice.Key == StringValue);
        return string.IsNullOrEmpty(selected.Key) ? string.Empty : GetChoiceName(selected.Value);
    }

    private static string GetChoiceName(string value) => value.Split('\n', 2)[0].TrimEnd('\r');

    private void SetApplicationInputText(string value) => this.RaiseAndSetIfChanged(ref _applicationInputText, value, nameof(ApplicationInputText));

    private void RefreshApplicationInputText()
    {
        if (IsApplicationSelector && !IsSelectorOpen && string.IsNullOrEmpty(SearchText))
            SetApplicationInputText(GetSelectedApplicationName());
    }

    private void HandleStringUpdate(string value)
    {
        _pendingStringValue = value;

        var delay = IsTextBasedSetting() ? _inputConfig.TextDebounceMs : ChoiceThrottleMs;

        _stringDebounceTimer?.Dispose();
        _stringDebounceTimer = new Timer(_ =>
        {
            var valueToSend = _pendingStringValue;
            if (valueToSend != null)
            {
                _ = SendSettingUpdateAsync(valueToSend);

                if (IsTextBasedSetting())
                {
                    Dispatcher.UIThread.Post(() => _isUserEditing = false);
                }
            }
        }, null, delay, Timeout.Infinite);
    }

    private void HandleNumberUpdate(object value)
    {
        _pendingNumberValue = value;

        _numberThrottleTimer?.Dispose();
        _numberThrottleTimer = new Timer(_ =>
        {
            var valueToSend = _pendingNumberValue;
            if (valueToSend != null)
            {
                _ = SendSettingUpdateAsync(valueToSend);
            }
        }, null, _inputConfig.NumberThrottleMs, Timeout.Infinite);
    }

    private void UpdateDisplayedChoices()
    {
        if (Setting.ControlType != SettingControlType.Choice)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateDisplayedChoices);
            return;
        }

        var currentValue = StringValue;
        var newDisplayList = new List<KeyValuePair<string, string>>(_rawChoices);

        if (IsApplicationSelector && !string.IsNullOrWhiteSpace(SearchText))
        {
            var matches = newDisplayList.Where(choice =>
                choice.Value.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                choice.Key.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0 && Setting.Key == "ApplicationPath")
            {
                var customApp = newDisplayList.FirstOrDefault(choice => choice.Key == "custom-app");
                if (!string.IsNullOrEmpty(customApp.Key))
                    matches.Add(customApp);
            }

            newDisplayList = matches;
        }

        var exists = newDisplayList.Any(c => c.Key == currentValue);

        if (!exists && !string.IsNullOrEmpty(currentValue) &&
            (!IsApplicationSelector || string.IsNullOrWhiteSpace(SearchText)))
        {
            newDisplayList.Insert(0, new KeyValuePair<string, string>(
                currentValue,
                $"{currentValue} (Saved)"
            ));

            newDisplayList.RemoveAll(x => string.IsNullOrEmpty(x.Key));
        }

        if (DisplayedChoices.SequenceEqual(newDisplayList))
        {
            this.RaisePropertyChanged(nameof(SelectedChoice));
            return;
        }

        DisplayedChoices.Clear();
        ApplicationChoices.Clear();
        foreach (var item in newDisplayList)
        {
            DisplayedChoices.Add(item);
            ApplicationChoices.Add(new ApplicationChoiceViewModel(item, SelectChoiceCommand, SelectorItemActionLabel));
        }

        this.RaisePropertyChanged(nameof(SelectedChoice));
    }

    private void UpdateMultiChoices()
    {
        if (Setting.ControlType != SettingControlType.MultiChoice)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateMultiChoices);
            return;
        }

        var currentList = Setting.GetCurrentValueAsObject() switch
        {
            List<string> list => list.ToHashSet(),
            string value when value.Length > 0 => value.Split('|').ToHashSet(),
            _ => []
        };

        MultiChoices.Clear();
        foreach (var choice in _rawChoices)
        {
            var isSelected = currentList.Contains(choice.Key);
            var itemVm = new MultiChoiceItemViewModel(choice.Key, choice.Value, isSelected);

            itemVm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MultiChoiceItemViewModel.IsSelected))
                {
                    OnMultiChoiceChanged();
                }
            };

            MultiChoices.Add(itemVm);
        }
    }

    private void OnMultiChoiceChanged()
    {
        var selectedKeys = MultiChoices.Where(x => x.IsSelected).Select(x => x.Key).ToList();

        Setting.SetValueFromObject(selectedKeys);

        var serialized = string.Join("|", selectedKeys);
        HandleStringUpdate(serialized);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task BrowseAsync()
    {
        if (_filePickerService == null)
        {
            return;
        }

        var result = Setting.ControlType switch
        {
            SettingControlType.FilePicker => await _filePickerService.PickFileAsync($"Select {Label}", Setting.Filter,
                StringValue),
            SettingControlType.DirectoryPicker => await _filePickerService.PickFolderAsync($"Select {Label}",
                StringValue),
            _ => null
        };

        if (!string.IsNullOrEmpty(result))
        {
            StringValue = result;
        }
    }

    private void LoadHistory()
    {
        if (!Setting.HasHistory || _uiConfig == null)
        {
            return;
        }

        if (!_uiConfig.InputHistory.TryGetValue(Setting.Key, out var items))
        {
            return;
        }

        foreach (var item in items)
        {
            History.Add(item);
        }
    }

    private void RemoveHistoryItem(string item)
    {
        if (!History.Remove(item)) return;

        if (_uiConfig == null || _uiSettingsStore == null)
        {
            return;
        }

        _uiConfig.InputHistory[Setting.Key] = [.. History];
        _uiSettingsStore.Save(_uiConfig);
    }

    private void TryAddToHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Setting.HasHistory || _uiSettingsStore == null || _uiConfig == null)
        {
            return;
        }

        var isValidPath = false;
        try
        {
            isValidPath = Setting.ControlType == SettingControlType.DirectoryPicker
                ? Directory.Exists(value)
                : File.Exists(value);
        }
        catch
        {
            // Ignore invalid path characters
        }

        if (!isValidPath)
        {
            return;
        }

        if (History.Contains(value))
        {
            History.Move(History.IndexOf(value), 0);
        }
        else
        {
            History.Insert(0, value);
        }

        while (History.Count > 5)
        {
            History.RemoveAt(History.Count - 1);
        }

        _uiConfig.InputHistory[Setting.Key] = [.. History];
        _uiSettingsStore.Save(_uiConfig);
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private bool IsTextBasedSetting()
    {
        return Setting.ControlType is
            SettingControlType.Text or
            SettingControlType.TextArea or
            SettingControlType.FilePicker or
            SettingControlType.DirectoryPicker or
            SettingControlType.Secret;
    }

    public void OnFocusGained()
    {
        if (IsTextBasedSetting())
        {
            _isUserEditing = true;
        }
    }

    public void OnFocusLost()
    {
        if (!IsTextBasedSetting())
        {
            return;
        }

        if (_isUserEditing && _inputConfig.FlushOnFocusLoss)
        {
            var currentValue = Setting.GetCurrentValueAsObject() as string;
            _ = SendSettingUpdateAsync(currentValue);
        }

        _isUserEditing = false;
    }

    private async Task SendSettingUpdateAsync(object? value)
    {
        try
        {
            await _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update setting {SettingKey}", Setting.Key);
        }
    }


    public void Dispose()
    {
        _stringDebounceTimer?.Dispose();
        _numberThrottleTimer?.Dispose();
    }
}

public sealed class ApplicationChoiceViewModel
{
    private static readonly ConcurrentDictionary<string, Bitmap> Icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _iconPath;

    public ApplicationChoiceViewModel(KeyValuePair<string, string> choice, ICommand selectCommand, string actionLabel)
    {
        Choice = choice;
        SelectCommand = selectCommand;
        SelectorItemActionLabel = actionLabel;

        var lines = choice.Value.Split('\n');
        Name = lines.ElementAtOrDefault(0)?.TrimEnd('\r') ?? string.Empty;
        Path = lines.ElementAtOrDefault(1)?.TrimEnd('\r') ?? string.Empty;
        _iconPath = (lines.Length > 2 ? lines[2] : lines.ElementAtOrDefault(1))?.TrimEnd('\r');
    }

    public KeyValuePair<string, string> Choice { get; }
    public ICommand SelectCommand { get; }
    public string SelectorItemActionLabel { get; }
    public string Name { get; }
    public string Path { get; }
    public Bitmap? Icon => GetIcon(_iconPath);

    private static Bitmap? GetIcon(string? path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return Icons.GetOrAdd(path, ExtractIcon);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap ExtractIcon(string path)
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path)
                         ?? throw new InvalidOperationException("No associated icon.");
        using var source = icon.ToBitmap();
        using var stream = new MemoryStream();
        source.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}

public class MultiChoiceItemViewModel(string key, string label, bool isSelected) : ReactiveObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [Reactive]
    public bool IsSelected { get; set; } = isSelected;
}
