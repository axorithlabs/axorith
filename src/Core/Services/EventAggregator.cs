using System.Collections.Concurrent;
using Axorith.Sdk.Services;
using Serilog;

namespace Axorith.Core.Services;

/// <summary>
///     A thread-safe implementation of the <see cref="IEventAggregator" /> interface.
///     It allows for publishing messages and subscribing to them in a decoupled manner.
/// </summary>
internal sealed class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, ConcurrentBag<WeakReference<object>>> _subscriptions = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        var eventType = typeof(TEvent);
        var handlers = _subscriptions.GetOrAdd(eventType, _ => []);

        // Use WeakReference to prevent memory leaks if a subscriber is garbage collected without unsubscribing.
        var weakHandler = new WeakReference<object>(handler);
        handlers.Add(weakHandler);

        return new Unsubscriber<TEvent>(this, handler);
    }

    public void Publish<TEvent>(TEvent eventMessage)
    {
        var eventType = typeof(TEvent);
        if (!_subscriptions.TryGetValue(eventType, out var handlers)) return;

        // Create snapshot for safe iteration
        var handlersList = handlers.ToList();
        var deadReferences = new List<WeakReference<object>>();

        foreach (var weakHandler in handlersList)
            if (weakHandler.TryGetTarget(out var handlerTarget) && handlerTarget is Action<TEvent> handler)
                try
                {
                    // Execute handler synchronously for predictable ordering
                    handler(eventMessage);
                }
                catch (Exception ex)
                {
                    // Silently ignore handler exceptions to not break other subscribers
                    Log.Warning(ex, "Event handler threw in EventAggregator for event type {EventType}",
                        typeof(TEvent).Name);
                }
            else
                deadReferences.Add(weakHandler);

        // Clean up dead references if any found
        if (deadReferences.Count > 0)
            CleanupDeadReferences(eventType);
    }

    private void CleanupDeadReferences(Type eventType)
    {
        if (!_subscriptions.TryGetValue(eventType, out var handlers)) return;

        // Create new bag with only live references
        var liveReferences = handlers.Where(wr => wr.TryGetTarget(out _)).ToList();
        _subscriptions[eventType] = [.. liveReferences];
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        var eventType = typeof(TEvent);
        if (!_subscriptions.TryGetValue(eventType, out var handlers)) return;

        // Filter out the handler to remove
        var filtered = handlers.Where(wh =>
            !wh.TryGetTarget(out var target) || !target.Equals(handler)
        ).ToList();

        _subscriptions[eventType] = [.. filtered];
    }

    private sealed class Unsubscriber<TEvent>(EventAggregator aggregator, Action<TEvent> handler) : IDisposable
    {
        public void Dispose()
        {
            aggregator.Unsubscribe(handler);
        }
    }
}