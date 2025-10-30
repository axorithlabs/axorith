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
    ///     A user-friendly message explaining the result, especially for warnings and errors.
    /// </summary>
    public string Message { get; }

    // --- Static factory methods for convenient result creation ---

    /// <summary>
    ///     Gets a pre-defined successful validation result.
    /// </summary>
    public static ValidationResult Success { get; } = new(ValidationStatus.Ok, "Configuration is valid.");

    /// <summary>
    ///     Creates a new failed validation result with a specific error message.
    /// </summary>
    /// <param name="errorMessage">The error message to display to the user.</param>
    public static ValidationResult Fail(string errorMessage)
    {
        return new ValidationResult(ValidationStatus.Error, errorMessage);
    }

    /// <summary>
    ///     Creates a new warning validation result with a specific message.
    /// </summary>
    /// <param name="warningMessage">The warning message to display to the user.</param>
    public static ValidationResult Warn(string warningMessage)
    {
        return new ValidationResult(ValidationStatus.Warning, warningMessage);
    }

    /// <summary>
    ///     Private constructor to enforce the use of static factory methods.
    /// </summary>
    private ValidationResult(ValidationStatus status, string message)
    {
        Status = status;
        Message = message;
    }
}