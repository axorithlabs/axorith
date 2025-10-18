using Serilog;
using Serilog.Core;

namespace Axorith.Core;

internal static class Logging
{
    public static Logger CreateLogger()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Axorith",
            "logs",
            "axorith-.log");

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .CreateLogger();
    }
}