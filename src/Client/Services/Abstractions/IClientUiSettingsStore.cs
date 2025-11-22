namespace Axorith.Client.Services.Abstractions;

public interface IClientUiSettingsStore
{
    ClientUiConfiguration LoadOrDefault();
    void Save(ClientUiConfiguration configuration);
}