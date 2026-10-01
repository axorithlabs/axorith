namespace Axorith.Benchmarks;

internal static class BenchmarkEnvironment
{
    private static readonly object Gate = new();
    private static string? _root;

    public static string Root
    {
        get
        {
            Initialize();
            return _root!;
        }
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_root is not null)
                return;

            _root = Path.Combine(Path.GetTempPath(), "AxorithBenchmarks",
                $"{Environment.ProcessId}-{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable("AXORITH_ROAMING_ROOT",
                Path.Combine(_root, "roaming", "Axorith"));
            Environment.SetEnvironmentVariable("AXORITH_LOCAL_ROOT",
                Path.Combine(_root, "local", "Axorith"));

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    if (Directory.Exists(_root))
                        Directory.Delete(_root, recursive: true);
                }
                catch
                {
                    // The operating system removes any remaining temporary benchmark files later.
                }
            };
        }
    }
}
