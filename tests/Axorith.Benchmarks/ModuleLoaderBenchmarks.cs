using Axorith.Core.Services;
using Axorith.Sdk;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Benchmarks;

[MemoryDiagnoser]
public class ModuleLoaderBenchmarks
{
    private readonly ModuleLoader _loader = new(NullLogger<ModuleLoader>.Instance);
    private string[] _searchPaths = null!;
    private IReadOnlyList<ModuleDefinition>? _definitions;

    [GlobalSetup]
    public void Setup() => _searchPaths = [Path.Combine(BenchmarkEnvironment.BuildOutputDirectory, "Modules")];

    [Benchmark]
    public async Task<int> DiscoverAndLoadProductionModules()
    {
        _definitions = await _loader.LoadModuleDefinitionsAsync(_searchPaths, CancellationToken.None);
        if (_definitions.Count != 4)
            throw new InvalidOperationException($"Expected four production modules, found {_definitions.Count}.");

        return _definitions.Count;
    }

    [IterationCleanup]
    public void UnloadModules()
    {
        if (_definitions is null)
            return;

        foreach (var definition in _definitions)
        {
            definition.ModuleType = null;
            definition.LoadContext?.Unload();
            definition.LoadContext = null;
        }

        _definitions = null;
    }
}
