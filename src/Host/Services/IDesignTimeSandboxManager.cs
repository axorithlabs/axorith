using Axorith.Sdk;

namespace Axorith.Host.Services;

/// <summary>
///     Interface for managing design-time module sandboxes.
///     Provides isolation for module configuration before session start.
/// </summary>
public interface IDesignTimeSandboxManager
{
    /// <summary>
    ///     Ensures a sandbox exists for the given module instance.
    ///     Creates one if it doesn't exist and initializes it with the given settings.
    /// </summary>
    Task EnsureAsync(Guid instanceId, Guid moduleId, IReadOnlyDictionary<string, string?> initial,
        CancellationToken ct);

    /// <summary>
    ///     Gets the module instance associated with the given sandbox ID.
    ///     Returns null if no sandbox exists.
    /// </summary>
    IModule? GetModule(Guid instanceId);

    /// <summary>
    ///     Applies a setting value to the sandbox module.
    /// </summary>
    void ApplySetting(Guid instanceId, string key, string? stringValue);

    /// <summary>
    ///     Tries to invoke an action on the sandbox module.
    /// </summary>
    Task<bool> TryInvokeActionAsync(Guid instanceId, string actionKey, CancellationToken ct);

    /// <summary>
    ///     Disposes a single sandbox by instance ID.
    /// </summary>
    void DisposeSandbox(Guid instanceId);

    /// <summary>
    ///     Disposes all sandboxes associated with the given preset's module instances.
    /// </summary>
    void DisposeSandboxesForPreset(IEnumerable<Guid> moduleInstanceIds);

    /// <summary>
    ///     Re-broadcasts all setting/action state for a sandbox to connected clients.
    /// </summary>
    void ReBroadcast(Guid instanceId);
}