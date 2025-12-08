using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Sdk.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class SettingViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Guid _moduleInstanceId;
    private readonly IModulesApi _modulesApi;
    private readonly Subject<object?> _numberUpdates = new();
    private readonly Subject<string?> _stringUpdates = new();
    private readonly Subject<Unit> _valueChangedSubject = new();

    private readonly IClientUiSettingsStore? _uiSettingsStore;
    private readonly ClientUiConfiguration? _uiConfig;
    private readonly IFilePickerService? _filePickerService;
    private readonly SettingsInputConfiguration _inputConfig;

    private const int ChoiceThrottleMs = 50;

    private bool _isUserEditing;

    private IReadOnlyList<KeyValuePair<string, string>> _rawChoices = [];

    public ISetting Setting { get; }

    public string Label
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsVisible
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public bool IsReadOnly
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? Error
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IObservable<Unit> ValueChanged => _valueChangedSubject.AsObservable();

    public ObservableCollection<string> History { get; } = [];

    public string? SelectedHistoryItem
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (!string.IsNullOrEmpty(value))
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
            this.RaisePropertyChanged();

            _stringUpdates.OnNext(value);
            _valueChangedSubject.OnNext(Unit.Default);
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

            _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, value);
            _valueChangedSubject.OnNext(Unit.Default);
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

            _numberUpdates.OnNext(boxedValue);
            _valueChangedSubject.OnNext(Unit.Default);
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
            this.RaisePropertyChanged(nameof(StringValue));
            this.RaisePropertyChanged();
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

        setting.Label
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(newLabel => Label = newLabel)
            .DisposeWith(_disposables);

        setting.IsVisible
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(visible => IsVisible = visible)
            .DisposeWith(_disposables);

        setting.IsReadOnly
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(readOnly => IsReadOnly = readOnly)
            .DisposeWith(_disposables);

        setting.ValueAsObject
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (ShouldIgnoreBroadcast())
                {
                    return;
                }

                this.RaisePropertyChanged(nameof(StringValue));
                this.RaisePropertyChanged(nameof(BoolValue));
                this.RaisePropertyChanged(nameof(DecimalValue));

                UpdateDisplayedChoices();
                UpdateMultiChoices();
            })
            .DisposeWith(_disposables);

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

        Setting.Choices?
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(c =>
            {
                _rawChoices = c;
                UpdateDisplayedChoices();
                UpdateMultiChoices();
            })
            .DisposeWith(_disposables);

        _numberUpdates
            .Throttle(TimeSpan.FromMilliseconds(_inputConfig.NumberThrottleMs))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(v => { _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, v); })
            .DisposeWith(_disposables);

        if (IsTextBasedSetting())
        {
            _stringUpdates
                .Throttle(TimeSpan.FromMilliseconds(_inputConfig.TextDebounceMs))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Subscribe(v =>
                {
                    _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, v);

                    Dispatcher.UIThread.Post(() => _isUserEditing = false);
                })
                .DisposeWith(_disposables);
        }
        else
        {
            _stringUpdates
                .Throttle(TimeSpan.FromMilliseconds(ChoiceThrottleMs))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Subscribe(v => { _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, v); })
                .DisposeWith(_disposables);
        }
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
                this.RaisePropertyChanged(nameof(SelectedChoice));
                return;
            }
        }

        DisplayedChoices.Clear();
        foreach (var item in newDisplayList)
        {
            DisplayedChoices.Add(item);
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

            itemVm.WhenAnyValue(x => x.IsSelected)
                .Skip(1)
                .Subscribe(_ => OnMultiChoiceChanged())
                .DisposeWith(_disposables);

            MultiChoices.Add(itemVm);
        }
    }

    private void OnMultiChoiceChanged()
    {
        var selectedKeys = MultiChoices.Where(x => x.IsSelected).Select(x => x.Key).ToList();

        Setting.SetValueFromObject(selectedKeys);

        var serialized = string.Join("|", selectedKeys);
        _stringUpdates.OnNext(serialized);
        _valueChangedSubject.OnNext(Unit.Default);
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

        _uiConfig.InputHistory[Setting.Key] = History.ToList();
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

        _uiConfig.InputHistory[Setting.Key] = History.ToList();
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

    public void Dispose()
    {
        _disposables.Dispose();
        _valueChangedSubject.Dispose();
        _numberUpdates.Dispose();
        _stringUpdates.Dispose();
    }
}

// Helper VM for MultiChoice items
public class MultiChoiceItemViewModel(string key, string label, bool isSelected) : ReactiveObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = isSelected;
}