using Axorith.Sdk.Settings;

namespace Axorith.Sdk;

/// <summary>
/// The main contract that every Axorith module must implement.
/// </summary>
public interface IModule : IDisposable
{
    /// <summary>
    /// A unique and constant identifier for the module.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The user-friendly name of the module.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A detailed description of what the module does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// A category for grouping modules in the UI.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Gets the set of operating systems that this module supports.
    /// The Axorith.Core will only load the module if the current OS is in this set.
    /// This property should return a <see cref="IReadOnlySet{Platform}"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// // For a cross-platform module:
    /// public IReadOnlySet&lt;Platform&gt; SupportedPlatforms => new HashSet&lt;Platform&gt; { Platform.Windows, Platform.Linux, Platform.MacOS };
    /// 
    /// // For a Windows-only module:
    /// public IReadOnlySet&lt;Platform&gt; SupportedPlatforms => new HashSet&lt;Platform&gt; { Platform.Windows };
    /// </code>
    /// </example>
    IReadOnlySet<Platform> SupportedPlatforms { get; }

    /// <summary>
    /// Gets the list of all available settings for this module.
    /// </summary>
    /// <returns>A read-only list of <see cref="SettingBase"/> definitions.</returns>
    IReadOnlyList<SettingBase> GetSettings();
    
    /// <summary>
    /// Asynchronously validates the provided user settings.
    /// </summary>
    /// <param name="userSettings">The settings provided by the user.</param>
    /// <param name="cancellationToken">A token to signal that the validation should be cancelled.</param>
    Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken);
    
    /// <summary>
    /// Optional. Gets the type of a custom Avalonia UserControl for rendering this module's settings.
    /// If this is not null, the Client will ignore GetSettings() and render this control instead.
    /// The DataContext for this control will be an object provided by GetSettingsViewModel().
    /// </summary>
    Type? CustomSettingsViewType { get; }

    /// <summary>
    /// Optional. Gets an object that will be used as the DataContext for the CustomSettingsView.
    /// This is only called if CustomSettingsViewType is not null.
    /// </summary>
    object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings);

    /// <summary>
    /// The asynchronous method that is called when a session starts.
    /// </summary>
    /// <param name="context">The context for interacting with the core.</param>
    /// <param name="userSettings">The settings provided by the user for this session.</param>
    /// <param name="cancellationToken">A token to signal that the start-up process should be cancelled.</param>
    Task OnSessionStartAsync(IModuleContext context, IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken);

    /// <summary>
    /// The asynchronous method that is called when a session ends.
    /// This is where the module should clean up its resources.
    /// </summary>
    /// <param name="context">The context for interacting with the core, primarily for logging the shutdown process.</param>
    Task OnSessionEndAsync(IModuleContext context);
}