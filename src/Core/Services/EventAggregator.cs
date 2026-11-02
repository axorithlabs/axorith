using System.Collections.Concurrent;
using System.Collections.Immutable;
using Axorith.Sdk.Services;
using Serilog;

namespace Axorith.Core.Services;

public sealed class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, ImmutableArray<WeakReference<object>>> _subscriptions = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        var reference = new WeakReference<object>(handler);
        _subscriptions.AddOrUpdate(typeof(TEvent), [reference], (_, handlers) => handlers.Add(reference));
        return new Unsubscriber<TEvent>(this, handler);
    }

    public void Publish<TEvent>(TEvent eventMessage)
    {
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers)) return;

        var hasDeadReferences = false;
        foreach (var reference in handlers)
        {
            if (reference.TryGetTarget(out var target) && target is Action<TEvent> handler)
                InvokeHandler(handler, eventMessage);
            else
                hasDeadReferences = true;
        }

        if (hasDeadReferences) RemoveHandlers<TEvent>(null);
    }

    public Task PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers)) return Task.CompletedTask;

        return Task.WhenAll(GetHandlers<TEvent>(handlers).Select(handler =>
            Task.Run(() => InvokeHandler(handler, eventMessage), cancellationToken)));
    }

    private IEnumerable<Action<TEvent>> GetHandlers<TEvent>(ImmutableArray<WeakReference<object>> references)
    {
        var hasDeadReferences = false;
        foreach (var reference in references)
        {
            if (reference.TryGetTarget(out var target) && target is Action<TEvent> handler)
                yield return handler;
            else
                hasDeadReferences = true;
        }

        if (hasDeadReferences) RemoveHandlers<TEvent>(null);
    }

    private static void InvokeHandler<TEvent>(Action<TEvent> handler, TEvent eventMessage)
    {
        try
        {
            handler(eventMessage);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Event handler threw in EventAggregator for event type {EventType}", typeof(TEvent).Name);
        }
    }

    private void RemoveHandlers<TEvent>(Action<TEvent>? handler) =>
        // Update the current list atomically so cleanup cannot overwrite a concurrent subscription.
        _subscriptions.AddOrUpdate(typeof(TEvent), [], (_, handlers) => handlers.RemoveAll(reference =>
            !reference.TryGetTarget(out var target) || target.Equals(handler)));

    private sealed class Unsubscriber<TEvent>(EventAggregator aggregator, Action<TEvent> handler) : IDisposable
    {
        public void Dispose() => aggregator.RemoveHandlers(handler);
    }
}
