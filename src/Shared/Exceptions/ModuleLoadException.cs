namespace Axorith.Shared.Exceptions;

/// <summary>
/// Thrown when a module fails to load for any reason (e.g., file is corrupt, type not found).
/// </summary>
public class ModuleLoadException(string message, string? modulePath = null, Exception? innerException = null)
    : AxorithException(message, innerException!)
{
    public string? ModulePath { get; } = modulePath;
}