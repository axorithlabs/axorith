using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Module.ApplicationLauncher;

internal interface ILauncherApp : IDisposable
{
    IReadOnlyList<ISetting> GetSettings();
    IReadOnlyList<IAction> GetActions();
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken);
    Task OnSessionStartAsync(CancellationToken cancellationToken);
    Task OnSessionEndAsync(CancellationToken cancellationToken = default);
}
