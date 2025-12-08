using BenchmarkDotNet.Attributes;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Benchmarks;

/// <summary>
///     Benchmarks for Action invocation and state changes.
///     Measures synchronous and asynchronous action performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ActionBenchmarks
{
    private Action _action = null!;
    private Action _actionWithHandler = null!;

    [GlobalSetup]
    public void Setup()
    {
        _action = Action.Create("test", "Test Action", isEnabled: true);
        _actionWithHandler = Action.Create("test-handler", "Test Action Handler", isEnabled: true);
        _actionWithHandler.OnInvokeAsync(async () => await Task.Delay(1));
    }

    [Benchmark(Baseline = true)]
    public void CreateAction()
    {
        var action = Action.Create("key", "Label", isEnabled: true);
    }

    [Benchmark]
    public void Invoke()
    {
        _action.Invoke();
    }

    [Benchmark]
    public async Task InvokeAsync()
    {
        await _action.InvokeAsync();
    }

    [Benchmark]
    public async Task InvokeAsyncWithHandler()
    {
        await _actionWithHandler.InvokeAsync();
    }

    [Benchmark]
    public void SetLabel()
    {
        _action.SetLabel("New Label");
    }

    [Benchmark]
    public void SetEnabled()
    {
        _action.SetEnabled(false);
        _action.SetEnabled(true);
    }

    [Benchmark]
    public string GetCurrentLabel()
    {
        return _action.GetCurrentLabel();
    }

    [Benchmark]
    public bool GetCurrentEnabled()
    {
        return _action.GetCurrentEnabled();
    }

    [Benchmark]
    public void SubscribeToInvoked()
    {
        var sub = _action.Invoked.Subscribe(_ => { });
        sub.Dispose();
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    public void MultipleInvokes(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _action.Invoke();
        }
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    public async Task MultipleAsyncInvokes(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _action.InvokeAsync();
        }
    }

    [Benchmark]
    public void InvokeWithMultipleSubscribers()
    {
        var action = Action.Create("multi", "Label", isEnabled: true);
        var subs = new List<IDisposable>();

        for (var i = 0; i < 10; i++)
        {
            subs.Add(action.Invoked.Subscribe(_ => { }));
        }

        action.Invoke();

        foreach (var sub in subs)
        {
            sub.Dispose();
        }
    }

    [Benchmark]
    public void DisabledInvoke()
    {
        var action = Action.Create("disabled", "Label", isEnabled: false);
        action.Invoke(); // Should be no-op
    }

    [Benchmark]
    public async Task DisabledAsyncInvoke()
    {
        var action = Action.Create("disabled", "Label", isEnabled: false);
        await action.InvokeAsync(); // Should be no-op
    }
}