namespace Axorith.Module.Test;

/// <summary>
///     A test event for demonstrating the EventAggregator functionality.
///     Used by the Test module to verify inter-module communication.
/// </summary>
public class TestEvent
{
    /// <summary>
    ///     Gets the message content of the event.
    /// </summary>
    public string Message { get; init; } = "";
}