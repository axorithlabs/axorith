using System.Reactive.Linq;
using Axorith.Sdk.Actions;
using Action = Axorith.Contracts.Action;

namespace Axorith.Host.Mappers;

/// <summary>
///     Maps between SDK IAction and protobuf Action messages.
/// </summary>
public static class ActionMapper
{
    public static Action ToMessage(IAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new Action
        {
            Key = action.Key,
            Label = action.Label.FirstAsync().Wait(),
            Description = string.Empty, // IAction doesn't have Description
            IsEnabled = action.IsEnabled.FirstAsync().Wait()
        };
    }
}