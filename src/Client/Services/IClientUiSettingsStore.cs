namespace Axorith.Client.Services;

public interface IClientUiSettingsStore
{
    ClientUiConfiguration LoadOrDefault();
    void Save(ClientUiConfiguration configuration);
}