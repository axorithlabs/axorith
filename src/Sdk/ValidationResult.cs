namespace Axorith.Sdk;

/// <summary>
///     Represents the result of a module's settings validation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    ///     The status of the validation.
    /// </summary>
    public ValidationStatus Status { get; }

    /// <summary>
    ///     A user-friendly message explaining the result (global error).
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Specific errors associated with setting keys.
    ///     Key: Setting Key, Value: Error Message.
    /// </summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; }

    /// <summary>
    ///     Gets a pre-defined successful validation result.
    /// </summary>
    public static ValidationResult Success { get; } = new(ValidationStatus.Ok, "Configuration is valid.");

    /// <summary>
    ///     Creates a new failed validation result with a specific global error message.
    /// </summary>
    public static ValidationResult Fail(string errorMessage)
    {
        return new ValidationResult(ValidationStatus.Error, errorMessage);
    }

    /// <summary>
    ///     Creates a new failed validation result with specific field errors.
    /// </summary>
    /// <param name="fieldErrors">Dictionary of setting keys and error messages.</param>
    /// <param name="globalMessage">Optional global message.</param>
    public static ValidationResult Fail(IDictionary<string, string> fieldErrors, string globalMessage = "Validation failed")
    {
        return new ValidationResult(ValidationStatus.Error, globalMessage, fieldErrors);
    }

    /// <summary>
    ///     Creates a new warning validation result.
    /// </summary>
    public static ValidationResult Warn(string warningMessage)
    {
        return new ValidationResult(ValidationStatus.Warning, warningMessage);
    }

    private ValidationResult(ValidationStatus status, string message, IDictionary<string, string>? fieldErrors = null)
    {
        Status = status;
        Message = message;
        FieldErrors = fieldErrors?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, string>();
    }
}