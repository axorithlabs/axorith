using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Sdk.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class SettingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Guid _moduleInstanceId;
    private readonly IModulesApi _modulesApi;
    private readonly IClientUiSettingsStore? _uiSettingsStore;
    private readonly ClientUiConfiguration? _uiConfig;
    private readonly IFilePickerService? _filePickerService;
    private readonly SettingsInputConfiguration _inputConfig;
    private readonly List<IDisposable> _disposables = [];

    private const int ChoiceThrottleMs = 50;

    private bool _isUserEditing;
    private IReadOnlyList<KeyValuePair<string, string>> _rawChoices = [];

    private Timer? _stringDebounceTimer;
    private Timer? _numberThrottleTimer;
    private string? _pendingStringValue;
    private object? _pendingNumberValue;

    public ISetting Setting { get; }

    private string _label = string.Empty;

    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    private bool _isVisible = true;

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    private bool _isReadOnly;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        private set => SetProperty(ref _isReadOnly, value);
    }

    private string? _error;

    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public event EventHandler? ValueChanged;

    public ObservableCollection<string> History { get; } = [];

    private string? _selectedHistoryItem;

    public string? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            if (SetProperty(ref _selectedHistoryItem, value) && !string.IsNullOrEmpty(value))
            {
                StringValue = value;
            }
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
            OnPropertyChanged();

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
            OnPropertyChanged();

            _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, value);
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
            OnPropertyChanged();

            HandleNumberUpdate(boxedValue);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<KeyValuePair<string, string>> DisplayedChoices { get; } = [];

    public ObservableCollection<MultiChoiceItemViewModel> MultiChoices { get; } = [];

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
            OnPropertyChanged(nameof(StringValue));
            OnPropertyChanged();
        }
    }

    public ICommand ClickCommand { get; }
    public ICommand RemoveHistoryItemCommand { get; }
    public ICommand BrowseCommand { get; }

    public SettingViewModel(
        ISetting setting,
        Guid moduleInstanceId,
        IModulesApi modulesApi,
        IServiceProvider? serviceProvider = null)
    {
        Setting = setting;
        _moduleInstanceId = moduleInstanceId;
        _modulesApi = modulesApi;

        _inputConfig = serviceProvider?.GetService<IOptions<Configuration>>()?.Value.Ui.SettingsInput
                       ?? new SettingsInputConfiguration();

        if (serviceProvider != null)
        {
            _uiSettingsStore = serviceProvider.GetService<IClientUiSettingsStore>();
            _filePickerService = serviceProvider.GetService<IFilePickerService>();
            if (_uiSettingsStore != null)
            {
                _uiConfig = _uiSettingsStore.LoadOrDefault();
                LoadHistory();
            }
        }

        ClickCommand = ReactiveCommand.Create(() => { BoolValue = true; });
        RemoveHistoryItemCommand = ReactiveCommand.Create<string>(RemoveHistoryItem);
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);

        _disposables.Add(Setting.Label.Subscribe(newLabel =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Label = newLabel;
            }
            else
            {
                Dispatcher.UIThread.Post(() => Label = newLabel);
            }
        }));

        _disposables.Add(Setting.IsVisible.Subscribe(visible =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                IsVisible = visible;
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsVisible = visible);
            }
        }));

        _disposables.Add(Setting.IsReadOnly.Subscribe(readOnly =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                IsReadOnly = readOnly;
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsReadOnly = readOnly);
            }
        }));

        _disposables.Add(Setting.ValueAsObject.Subscribe(_ =>
        {
            if (ShouldIgnoreBroadcast())
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                OnPropertyChanged(nameof(StringValue));
                OnPropertyChanged(nameof(BoolValue));
                OnPropertyChanged(nameof(DecimalValue));
                UpdateDisplayedChoices();
                UpdateMultiChoices();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(StringValue));
                    OnPropertyChanged(nameof(BoolValue));
                    OnPropertyChanged(nameof(DecimalValue));
                    UpdateDisplayedChoices();
                    UpdateMultiChoices();
                });
            }
        }));

        if (setting.GetCurrentChoices() is { } initialChoices)
        {
            _rawChoices = initialChoices;
        }

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
            });
        }

        if (Setting.Choices != null)
        {
            _disposables.Add(Setting.Choices.Subscribe(c =>
            {
                _rawChoices = c;
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
                    });
                }
            }));
        }
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
                _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, valueToSend);

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
                _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, valueToSend);
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

        var exists = newDisplayList.Any(c => c.Key == currentValue);

        if (!exists && !string.IsNullOrEmpty(currentValue))
        {
            newDisplayList.Insert(0, new KeyValuePair<string, string>(
                currentValue,
                $"{currentValue} (Saved)"
            ));

            newDisplayList.RemoveAll(x => string.IsNullOrEmpty(x.Key));
        }

        if (DisplayedChoices.Count == newDisplayList.Count)
        {
            var identical = !DisplayedChoices
                .Where((t, i) => t.Key != newDisplayList[i].Key || t.Value != newDisplayList[i].Value).Any();
            if (identical)
            {
                OnPropertyChanged(nameof(SelectedChoice));
                return;
            }
        }

        DisplayedChoices.Clear();
        foreach (var item in newDisplayList)
        {
            DisplayedChoices.Add(item);
        }

        OnPropertyChanged(nameof(SelectedChoice));
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

        var currentList = new HashSet<string>();
        if (Setting.GetCurrentValueAsObject() is List<string> list)
        {
            foreach (var item in list)
            {
                currentList.Add(item);
            }
        }
        else if (Setting.GetCurrentValueAsObject() is string s && !string.IsNullOrEmpty(s))
        {
            foreach (var item in s.Split('|'))
            {
                currentList.Add(item);
            }
        }

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
        if (!History.Contains(item))
        {
            return;
        }

        History.Remove(item);

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

    /// <summary>
    ///     Determines if this setting is a text-based input type that should use debounce.
    /// </summary>
    private bool IsTextBasedSetting()
    {
        return Setting.ControlType is
            SettingControlType.Text or
            SettingControlType.TextArea or
            SettingControlType.FilePicker or
            SettingControlType.DirectoryPicker or
            SettingControlType.Secret;
    }

    private bool ShouldIgnoreBroadcast()
    {
        return _isUserEditing && IsTextBasedSetting();
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
            _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, currentValue);
        }

        _isUserEditing = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _stringDebounceTimer?.Dispose();
        _numberThrottleTimer?.Dispose();
    }
}

// Helper VM for MultiChoice items
public class MultiChoiceItemViewModel : INotifyPropertyChanged
{
    public string Key { get; }
    public string Label { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public MultiChoiceItemViewModel(string key, string label, bool isSelected)
    {
        Key = key;
        Label = label;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}