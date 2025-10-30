namespace Axorith.Sdk.Services;

/// <summary>
///     Provides a loosely-coupled messaging system for different parts of the application,
///     especially for inter-module communication.
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    ///     Subscribes a handler to an event of a specific type.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to subscribe to.</typeparam>
    /// <param name="handler">The action to execute when the event is published.</param>
    /// <returns>An IDisposable that can be used to unsubscribe the handler.</returns>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    /// <summary>
    ///     Publishes an event to all subscribed handlers.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <param name="eventMessage">The event object to publish.</param>
    void Publish<TEvent>(TEvent eventMessage);
}