namespace Axorith.Sdk.Settings;

/// <summary>
///     Defines the UI control to be used for rendering a setting.
/// </summary>
public enum SettingControlType
{
    /// <summary>
    ///     A standard single-line text input.
    /// </summary>
    Text,

    /// <summary>
    ///     A standard multi-line text input
    /// </summary>
    TextArea,

    /// <summary>
    ///     A checkbox for true/false values.
    /// </summary>
    Checkbox,

    /// <summary>
    ///     A field for numeric input.
    /// </summary>
    Number,

    /// <summary>
    ///     A dropdown list of choices.
    /// </summary>
    Choice,

    /// <summary>
    ///     A text input that masks its content (password field).
    ///     Secret settings use Ephemeral persistence and are stored in SecureStorage (Windows DPAPI),
    ///     never in preset JSON files. This ensures sensitive data like API tokens are protected.
    /// </summary>
    Secret,

    /// <summary>
    ///     A text input with a button to open a file browser.
    /// </summary>
    FilePicker,

    /// <summary>
    ///     A text input with a button to open a directory browser.
    /// </summary>
    DirectoryPicker,

    /// <summary>
    ///     A clickable button that performs an action.
    /// </summary>
    Button
}