namespace Axorith.Sdk;

/// <summary>Optional module lifecycle hooks for a permitted commitment break.</summary>
public interface ISessionBreakParticipant
{
    /// <summary>Temporarily suspends this module's restrictions for an allowed break.</summary>
    Task PauseForBreakAsync(CancellationToken cancellationToken);

    /// <summary>Restores this module's restrictions when the break ends.</summary>
    Task ResumeAfterBreakAsync(CancellationToken cancellationToken);
}
