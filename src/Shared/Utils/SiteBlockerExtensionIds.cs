namespace Axorith.Shared.Utils;

public static class SiteBlockerExtensionIds
{
    public const string Firefox = "site-blocker-firefox@axorithlabs.com";

    public static string ScopePipeName(string pipeName)
    {
        var prefix = Environment.GetEnvironmentVariable("AXORITH_TEST_PIPE_PREFIX");
        return string.IsNullOrEmpty(prefix) ? pipeName : $"{prefix}-{pipeName}";
    }
}
