namespace Axorith.Sdk;

/// <summary>Exposes the current browser rules for native messaging reconnections.</summary>
public interface IBrowserBlockingState
{
    /// <summary>Gets active rules, or null while blocking is inactive or paused.</summary>
    BrowserBlockingRules? BrowserRules { get; }
}

/// <summary>Describes one active browser restriction.</summary>
/// <param name="Mode">BlockList or AllowList.</param>
/// <param name="Sites">The domains used by this restriction.</param>
public sealed record BrowserBlockingRules(string Mode, IReadOnlyList<string> Sites);
