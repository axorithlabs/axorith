using System.Collections.Concurrent;
using Axorith.Sdk.Services;

namespace Axorith.Core.Services;

/// <summary>
/// A thread-safe implementation of the <see cref="IEventAggregator"/> interface.
/// It allows for publishing messages and subscribing to them in a decoupled manner.
/// </summary>
internal sealed class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, List<WeakReference<object>>> _subscriptions = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        var eventType = typeof(TEvent);
        var handlers = _subscriptions.GetOrAdd(eventType, _ => new List<WeakReference<object>>());

        // Use WeakReference to prevent memory leaks if a subscriber is garbage collected without unsubscribing.
        var weakHandler = new WeakReference<object>(handler);

        lock (handlers)
        {
            handlers.Add(weakHandler);
        }

        return new Unsubscriber<TEvent>(this, handler);
    }

    public void Publish<TEvent>(TEvent eventMessage)
    {
        var eventType = typeof(TEvent);
        if (!_subscriptions.TryGetValue(eventType, out var handlers)) return;

        List<WeakReference<object>> handlersToRemove = new();

        lock (handlers)
        {
            foreach (var weakHandler in handlers)
            {
                if (weakHandler.TryGetTarget(out var handlerTarget) && handlerTarget is Action<TEvent> handler)
                {
                    // Run the handler in the background to avoid blocking the publisher.
                    // In a more complex system, this could be a dedicated thread pool or a message queue.
                    ThreadPool.QueueUserWorkItem(_ => handler(eventMessage));
                }
                else
                {
                    // The subscriber has been garbage collected, so we should clean up.
                    handlersToRemove.Add(weakHandler);
                }
            }

            foreach (var toRemove in handlersToRemove)
            {
                handlers.Remove(toRemove);
            }
        }
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        var eventType = typeof(TEvent);
        if (!_subscriptions.TryGetValue(eventType, out var handlers)) return;

        lock (handlers)
        {
            var handlerToRemove = handlers.FirstOrDefault(wh => wh.TryGetTarget(out var target) && target.Equals(handler));
            if (handlerToRemove != null)
            {
                handlers.Remove(handlerToRemove);
            }
        }
    }

    private sealed class Unsubscriber<TEvent> : IDisposable
    {
        private readonly EventAggregator _aggregator;
        private readonly Action<TEvent> _handler;

        public Unsubscriber(EventAggregator aggregator, Action<TEvent> handler)
        {
            _aggregator = aggregator;
            _handler = handler;
        }

        public void Dispose()
        {
            _aggregator.Unsubscribe(_handler);
        }
    }
}