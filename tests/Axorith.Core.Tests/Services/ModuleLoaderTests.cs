using System.Text.Json;
using Axorith.Core.Services;
using Axorith.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

public sealed class ModuleLoaderTests : IDisposable
{
    private readonly ModuleLoader _loader = new(NullLogger<ModuleLoader>.Instance);
    private readonly string _modulesRoot = Path.Combine(Path.GetTempPath(), $"axorith-modules-{Guid.NewGuid():N}");

    public ModuleLoaderTests() => Directory.CreateDirectory(_modulesRoot);

    [Fact]
    public async Task LoadsAnInstalledModuleFromItsManifestAndAssembly()
    {
        var id = Guid.NewGuid();
        var moduleDirectory = CreateModule(_modulesRoot, "SiteBlocker", id, CurrentPlatform);

        var definitions = await _loader.LoadModuleDefinitionsAsync([_modulesRoot], CancellationToken.None);

        var definition = definitions.Should().ContainSingle().Which;
        definition.Id.Should().Be(id);
        definition.Name.Should().Be("SiteBlocker");
        definition.ModuleType.Should().NotBeNull();
        typeof(IModule).IsAssignableFrom(definition.ModuleType!).Should().BeTrue();
        definition.AssemblyPath.Should().Be(Path.Combine(moduleDirectory, "Axorith.Module.SiteBlocker.dll"));
        Release(definitions);
    }

    [Fact]
    public async Task SkipsMalformedOversizedAndUnsupportedManifests()
    {
        var malformed = Path.Combine(_modulesRoot, "Malformed");
        Directory.CreateDirectory(malformed);
        await File.WriteAllTextAsync(Path.Combine(malformed, "module.json"), "{ broken");

        var oversized = Path.Combine(_modulesRoot, "Oversized");
        Directory.CreateDirectory(oversized);
        await File.WriteAllTextAsync(Path.Combine(oversized, "module.json"), new string('x', 11 * 1024));

        var wrongPlatform = OperatingSystem.IsWindows() ? "Linux" : "Windows";
        CreateModule(_modulesRoot, "Unsupported", Guid.NewGuid(), wrongPlatform, copyAssembly: false);

        var definitions = await _loader.LoadModuleDefinitionsAsync([_modulesRoot], CancellationToken.None);

        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectsAssemblyPathsThatEscapeTheModuleDirectory()
    {
        var outsideAssembly = Path.Combine(_modulesRoot, "outside.dll");
        File.Copy(typeof(Axorith.Module.SiteBlocker.Module).Assembly.Location, outsideAssembly);
        var moduleDirectory = Path.Combine(_modulesRoot, "Escaping");
        Directory.CreateDirectory(moduleDirectory);
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "module.json"), JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            name = "Escaping",
            platforms = new[] { CurrentPlatform },
            assembly = "../outside.dll"
        }));

        var definitions = await _loader.LoadModuleDefinitionsAsync([_modulesRoot], CancellationToken.None);

        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadsModulesFromEverySearchPathAndStopsOnCancellation()
    {
        var firstPath = Path.Combine(_modulesRoot, "first");
        var secondPath = Path.Combine(_modulesRoot, "second");
        Directory.CreateDirectory(firstPath);
        Directory.CreateDirectory(secondPath);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        CreateModule(firstPath, "First", firstId, CurrentPlatform);
        CreateModule(secondPath, "Second", secondId, CurrentPlatform);

        var definitions = await _loader.LoadModuleDefinitionsAsync([firstPath, secondPath], CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledDefinitions = await _loader.LoadModuleDefinitionsAsync([firstPath], cancelled.Token);

        definitions.Select(module => module.Id).Should().BeEquivalentTo([firstId, secondId]);
        cancelledDefinitions.Should().BeEmpty();
        Release(definitions);
    }

    private static string CreateModule(string searchPath, string name, Guid id, string platform,
        bool copyAssembly = true)
    {
        var moduleDirectory = Path.Combine(searchPath, name);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyName = "Axorith.Module.SiteBlocker.dll";
        if (copyAssembly)
        {
            File.Copy(typeof(Axorith.Module.SiteBlocker.Module).Assembly.Location,
                Path.Combine(moduleDirectory, assemblyName));
        }

        File.WriteAllText(Path.Combine(moduleDirectory, "module.json"), JsonSerializer.Serialize(new
        {
            id,
            name,
            platforms = new[] { platform },
            assembly = assemblyName
        }));
        return moduleDirectory;
    }

    private static string CurrentPlatform => OperatingSystem.IsWindows()
        ? "Windows"
        : OperatingSystem.IsLinux()
            ? "Linux"
            : OperatingSystem.IsMacOS()
                ? "MacOs"
                : "Unknown";

    private void Release(IEnumerable<ModuleDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            var context = definition.LoadContext;
            if (context is not null)
                context.Unload();
            definition.LoadContext = null;
            definition.ModuleType = null;
        }
    }

    public void Dispose()
    {
        for (var attempt = 0; Directory.Exists(_modulesRoot); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            try
            {
                Directory.Delete(_modulesRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 49)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 49)
            {
                Thread.Sleep(50);
            }
        }
    }
}
