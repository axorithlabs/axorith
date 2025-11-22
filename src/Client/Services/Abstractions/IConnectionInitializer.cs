using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services.Abstractions;

public interface IConnectionInitializer
{
    Task InitializeAsync(App app, Configuration config, ILoggerFactory loggerFactory, ILogger<App> logger);
}