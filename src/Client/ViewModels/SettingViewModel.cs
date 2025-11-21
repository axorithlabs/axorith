using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Client.Services;
using Axorith.Sdk.Settings;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class SettingViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Guid _moduleInstanceId;
    private readonly IModulesApi _modulesApi;
    private readonly Subject<object?> _numberUpdates = new();
    private readonly Subject<string?> _stringUpdates = new();

    private readonly IClientUiSettingsStore? _uiSettingsStore;
    private readonly ClientUiConfiguration? _uiConfig;
    private readonly IFilePickerService? _filePickerService;

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
            if (string.Equals(current, value, StringComparison.Ordinal)) return;

            Setting.SetValueFromString(value);
            this.RaisePropertyChanged();

            _stringUpdates.OnNext(value);
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
            if (current == value) return;

            Setting.SetValueFromObject(value);
            this.RaisePropertyChanged();

            _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, value);
        }
    }

    public decimal DecimalValue
    {
        get
        {
            var value = Setting.GetCurrentValueAsObject();
            if (value == null) return 0;

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
            if (current != null && current.Equals(boxedValue)) return;

            Setting.SetValueFromObject(boxedValue);
            this.RaisePropertyChanged();

            _numberUpdates.OnNext(boxedValue);
        }
    }

    public ObservableCollection<KeyValuePair<string, string>> DisplayedChoices { get; } = [];

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
            if (!value.HasValue) return;
            if (value.Value.Key == StringValue) return;

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
                this.RaisePropertyChanged(nameof(StringValue));
                this.RaisePropertyChanged(nameof(BoolValue));
                this.RaisePropertyChanged(nameof(DecimalValue));
                
                UpdateDisplayedChoices();
            })
            .DisposeWith(_disposables);
        
        if (setting.GetCurrentChoices() is { } initialChoices)
        {
            _rawChoices = initialChoices;
        }
        
        if (Dispatcher.UIThread.CheckAccess())
            UpdateDisplayedChoices();
        else
            Dispatcher.UIThread.Post(UpdateDisplayedChoices);

        Setting.Choices?
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(c => 
            {
                _rawChoices = c;
                UpdateDisplayedChoices();
            })
            .DisposeWith(_disposables);

        _numberUpdates
            .Throttle(TimeSpan.FromMilliseconds(75))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(v => { _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, v); })
            .DisposeWith(_disposables);

        _stringUpdates
            .Throttle(TimeSpan.FromMilliseconds(250))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(v => { _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, v); })
            .DisposeWith(_disposables);
    }

    private void UpdateDisplayedChoices()
    {
        if (Setting.ControlType != SettingControlType.Choice) return;

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
            var identical = !DisplayedChoices.Where((t, i) => t.Key != newDisplayList[i].Key || t.Value != newDisplayList[i].Value).Any();
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

    private async Task BrowseAsync()
    {
        if (_filePickerService == null) return;

        string? result = Setting.ControlType switch
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
        if (!Setting.HasHistory || _uiConfig == null) return;

        if (_uiConfig.InputHistory.TryGetValue(Setting.Key, out var items))
        {
            foreach (var item in items)
            {
                History.Add(item);
            }
        }
    }

    private void RemoveHistoryItem(string item)
    {
        if (!History.Contains(item)) return;

        History.Remove(item);

        if (_uiConfig == null || _uiSettingsStore == null) return;

        _uiConfig.InputHistory[Setting.Key] = History.ToList();
        _uiSettingsStore.Save(_uiConfig);
    }

    private void TryAddToHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Setting.HasHistory || _uiSettingsStore == null || _uiConfig == null)
            return;

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

        if (!isValidPath) return;

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

    public void Dispose()
    {
        _disposables.Dispose();
    }
}