using Axorith.Sdk.Actions;
using Action = Axorith.Contracts.Action;

namespace Axorith.Host.Mappers;

public static class ActionMapper
{
    public static Action ToMessage(IAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new Action
        {
            Key = action.Key,
            Label = action.GetCurrentLabel(),
            Description = string.Empty, // IAction doesn't have Description
            IsEnabled = action.GetCurrentEnabled(),
            SettingKey = action.SettingKey ?? string.Empty
        };
    }
}
