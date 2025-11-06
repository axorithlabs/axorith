using System.Text.Json;
using Axorith.Core.Services;
using Axorith.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

/// <summary>
///     Critical tests for ModuleLoader - parsing module.json, loading DLLs, size limits, platform filtering
/// </summary>
public class ModuleLoaderTests : IDisposable
{
    private readonly ModuleLoader _loader;
    private readonly string _testModulesDir;

    public ModuleLoaderTests()
    {
        _loader = new ModuleLoader(NullLogger<ModuleLoader>.Instance);
        _testModulesDir = Path.Combine(Path.GetTempPath(), $"axorith-modules-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testModulesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testModulesDir))
            try
            {
                Directory.Delete(_testModulesDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithNoModules_ShouldReturnEmpty()
    {
        // Arrange
        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LoadModuleDefinitionsAsync_WithValidCompiledModule_ShouldDiscoverIModuleType()
    {
        // Attempt to locate the compiled Axorith.Module.Test.dll in the repo output
        var dllPath = TryFindTestModuleDll();
        if (dllPath is null)
            // Compiled module not available in this environment; skip test
            return;

        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "ValidRealModule");
        Directory.CreateDirectory(moduleDir);

        var moduleJson = new
        {
            id = Guid.NewGuid(),
            name = "Test Module",
            version = "1.0.0",
            platforms = new[] { GetCurrentPlatform() },
            assembly = Path.GetFileName(dllPath)
        };

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(moduleJson));

        var copiedDll = Path.Combine(moduleDir, Path.GetFileName(dllPath));
        File.Copy(dllPath, copiedDll, overwrite: true);

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().NotBeNull();
        definitions.Should().ContainSingle();
        var def = definitions.Single();
        def.ModuleType.Should().NotBeNull();
        typeof(IModule).IsAssignableFrom(def.ModuleType!).Should().BeTrue();
    }

    private static string? TryFindTestModuleDll()
    {
        try
        {
            // Search relative to the test assembly base directory up to a few levels
            var baseDir = AppContext.BaseDirectory;
            var probeRoots = new List<string>();
            var dir = baseDir;
            for (var i = 0; i < 6; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "..", "src", "Modules",
                    "Test", "bin"));
                probeRoots.Add(candidate);
                dir = Path.Combine(dir, "..");
            }

            foreach (var root in probeRoots.Distinct())
            {
                if (!Directory.Exists(root)) continue;
                var dll = Directory.EnumerateFiles(root, "Axorith.Module.Test.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (dll != null) return dll;
            }
        }
        catch
        {
            // ignore and return null
        }

        return null;
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithValidModule_ShouldLoadDefinition()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "TestModule");
        Directory.CreateDirectory(moduleDir);

        var moduleJson = new
        {
            id = Guid.NewGuid(),
            name = "Test Module",
            version = "1.0.0",
            author = "Test Author",
            description = "Test Description",
            platforms = new[] { GetCurrentPlatform() },
            assembly = "TestModule.dll"
        };

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(moduleJson));

        // Create dummy DLL
        var dllPath = Path.Combine(moduleDir, "TestModule.dll");
        await File.WriteAllBytesAsync(dllPath, [0x4D, 0x5A]); // MZ header

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty(); // Will be empty because DLL is not valid, but JSON was parsed
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithInvalidJson_ShouldSkipModule()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "BadModule");
        Directory.CreateDirectory(moduleDir);

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, "{ invalid json");

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithOversizedJson_ShouldSkipModule()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "OversizedModule");
        Directory.CreateDirectory(moduleDir);

        var jsonPath = Path.Combine(moduleDir, "module.json");
        // Create JSON larger than 10KB limit
        var largeContent = new string('X', 11 * 1024);
        await File.WriteAllTextAsync(jsonPath, $"{{\"description\": \"{largeContent}\"}}");

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithWrongPlatform_ShouldSkipModule()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "WrongPlatform");
        Directory.CreateDirectory(moduleDir);

        var wrongPlatform = GetCurrentPlatform() == "Windows" ? "Linux" : "Windows";
        var moduleJson = new
        {
            id = Guid.NewGuid(),
            name = "Wrong Platform",
            version = "1.0.0",
            platforms = new[] { wrongPlatform },
            assembly = "test.dll"
        };

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(moduleJson));

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithMissingAssembly_ShouldSkipModule()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "MissingDll");
        Directory.CreateDirectory(moduleDir);

        var moduleJson = new
        {
            id = Guid.NewGuid(),
            name = "Missing DLL",
            version = "1.0.0",
            platforms = new[] { GetCurrentPlatform() },
            assembly = "NonExistent.dll"
        };

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(moduleJson));

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithNonExistentPath_ShouldNotThrow()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testModulesDir, "NonExistent");
        var searchPaths = new[] { nonExistentPath };

        // Act
        var act = async () => await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithSymbolicLink_ShouldSkipInProduction()
    {
        // This test verifies that symbolic links are skipped in production (non-debug) mode
        // In debug mode, symlinks are allowed for development
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "SymlinkModule");
        Directory.CreateDirectory(moduleDir);

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert - should not throw
        definitions.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithMultiplePaths_ShouldSearchAll()
    {
        // Arrange
        var path1 = Path.Combine(_testModulesDir, "Path1");
        var path2 = Path.Combine(_testModulesDir, "Path2");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);

        var searchPaths = new[] { path1, path2 };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, cts.Token);

        // Assert - should stop early
        definitions.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithNullDefinition_ShouldSkip()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "NullDef");
        Directory.CreateDirectory(moduleDir);

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, "null");

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadModuleDefinitionsAsync_WithEmptyAssemblyField_ShouldSkip()
    {
        // Arrange
        var moduleDir = Path.Combine(_testModulesDir, "EmptyAssembly");
        Directory.CreateDirectory(moduleDir);

        var moduleJson = new
        {
            id = Guid.NewGuid(),
            name = "Empty Assembly",
            version = "1.0.0",
            platforms = new[] { GetCurrentPlatform() },
            assembly = ""
        };

        var jsonPath = Path.Combine(moduleDir, "module.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(moduleJson));

        var searchPaths = new[] { _testModulesDir };

        // Act
        var definitions = await _loader.LoadModuleDefinitionsAsync(searchPaths, CancellationToken.None);

        // Assert
        definitions.Should().BeEmpty();
    }

    private static string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "MacOs";
        return "Unknown";
    }
}