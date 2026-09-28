namespace Axorith.Sdk;

/// <summary>Optional runtime health check for modules required by a committed session.</summary>
public interface ICommittedSessionValidator
{
    /// <summary>Whether the module is reporting a degraded, but still operating, protection state.</summary>
    bool IsProtectionDegraded => false;

    /// <summary>Short runtime details for the active protection status.</summary>
    string? ProtectionStatusMessage => null;

    /// <summary>Checks whether the module can provide protection before it is started.</summary>
    Task<bool> CanStartCommittedSessionAsync(CancellationToken cancellationToken);

    /// <summary>Returns whether the module's required protection is still available.</summary>
    Task<bool> IsProtectionHealthyAsync(CancellationToken cancellationToken);
}
