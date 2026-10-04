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
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);

    /// <summary>
    ///     Publishes an event to all subscribed handlers.
    /// </summary>
    void Publish<TEvent>(TEvent eventMessage);

    /// <summary>
    ///     Publishes an event to all subscribed handlers asynchronously.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default);
}
