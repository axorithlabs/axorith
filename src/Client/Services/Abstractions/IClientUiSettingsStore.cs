namespace Axorith.Client.Services.Abstractions;

public interface IClientUiSettingsStore
{
    ClientUiConfiguration LoadOrDefault();
    bool Save(ClientUiConfiguration configuration);
}
