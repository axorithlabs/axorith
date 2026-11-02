using System.Runtime.CompilerServices;

namespace Axorith.Tests;

internal static class TestEnvironment
{
    // Linked into every test and benchmark assembly, including direct IDE/vstest runs.
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("AXORITH_TEST_MODE", "1");
        Environment.SetEnvironmentVariable("AXORITH_DEV", "1");
        Environment.SetEnvironmentVariable("AXORITH_TEST_PIPE_PREFIX", "axorith-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AXORITH_TELEMETRY_DISABLED", "1");
        Environment.SetEnvironmentVariable("AXORITH_TELEMETRY_API_KEY", "##E2E_DISABLED##");
        Environment.SetEnvironmentVariable("AXORITH_TELEMETRY_HOST", "http://127.0.0.1:9");
    }
}
