using Axorith.Sdk.Settings;
using BenchmarkDotNet.Attributes;

namespace Axorith.Benchmarks;

/// <summary>
///     Benchmarks for reactive Setting operations.
///     Measures the performance of value changes, subscriptions, and reactive notifications.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class SettingBenchmarks
{
    private Setting<string> _textSetting = null!;
    private Setting<int> _intSetting = null!;
    private Setting<bool> _boolSetting = null!;
    private Setting<string> _choiceSetting = null!;
    private List<IDisposable> _subscriptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        _textSetting = Setting.AsText("text", "Label", "default");
        _intSetting = Setting.AsInt("int", "Label", 0);
        _boolSetting = Setting.AsCheckbox("bool", "Label", false);

        var choices = new List<KeyValuePair<string, string>>
        {
            new("a", "Option A"),
            new("b", "Option B"),
            new("c", "Option C")
        };
        _choiceSetting = Setting.AsChoice("choice", "Label", "a", choices);

        _subscriptions = [];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
    }

    [Benchmark(Baseline = true)]
    public void SetTextValue()
    {
        _textSetting.SetValue("new value");
    }

    [Benchmark]
    public void SetIntValue()
    {
        _intSetting.SetValue(42);
    }

    [Benchmark]
    public void SetBoolValue()
    {
        _boolSetting.SetValue(true);
    }

    [Benchmark]
    public string GetTextValue()
    {
        return _textSetting.GetCurrentValue();
    }

    [Benchmark]
    public int GetIntValue()
    {
        return _intSetting.GetCurrentValue();
    }

    [Benchmark]
    public void SetValueFromString()
    {
        ((ISetting)_intSetting).SetValueFromString("123");
    }

    [Benchmark]
    public string GetValueAsString()
    {
        return ((ISetting)_intSetting).GetValueAsString();
    }

    [Benchmark]
    public void SetLabel()
    {
        _textSetting.SetLabel("New Label");
    }

    [Benchmark]
    public void SetVisibility()
    {
        _textSetting.SetVisibility(false);
    }

    [Benchmark]
    public void SetReadOnly()
    {
        _textSetting.SetReadOnly(true);
    }

    [Benchmark]
    public void SetChoices()
    {
        var newChoices = new List<KeyValuePair<string, string>>
        {
            new("x", "Option X"),
            new("y", "Option Y")
        };
        _choiceSetting.SetChoices(newChoices);
    }

    [Benchmark]
    public void SubscribeAndDispose()
    {
        var sub = _textSetting.Value.Subscribe(_ => { });
        sub.Dispose();
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public void MultipleValueChanges(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _intSetting.SetValue(i);
        }
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    public void MultipleSubscribers(int subscriberCount)
    {
        var setting = Setting.AsInt("bench", "Label", 0);
        var subs = new List<IDisposable>();

        for (var i = 0; i < subscriberCount; i++)
        {
            subs.Add(setting.Value.Subscribe(_ => { }));
        }

        setting.SetValue(42);

        foreach (var sub in subs)
        {
            sub.Dispose();
        }
    }
}

