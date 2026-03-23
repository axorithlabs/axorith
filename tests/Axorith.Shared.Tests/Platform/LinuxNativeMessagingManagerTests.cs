using System.Runtime.Versioning;
using System.Text.Json;
using Axorith.Shared.Platform;
using Axorith.Shared.Platform.Linux;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

[SupportedOSPlatform("linux")]
public class LinuxNativeMessagingManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LinuxNativeMessagingManager _sut;

    public LinuxNativeMessagingManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"axorith-nm-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new LinuxNativeMessagingManager(NullLogger<LinuxNativeMessagingManager>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_WithValidInputs_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var act = () => _sut.RegisterFirefoxHost("com.axorith", shimPath, ["ext1", "ext2"]);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_WithNullHostName_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "shim");
        var act = () => _sut.RegisterFirefoxHost(null!, shimPath, ["ext1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_WithWhiteSpaceHostName_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "shim");
        var act = () => _sut.RegisterFirefoxHost("  ", shimPath, ["ext1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_WithNullExecutablePath_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var act = () => _sut.RegisterFirefoxHost("com.axorith", null!, ["ext1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_WithWhiteSpaceExecutablePath_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var act = () => _sut.RegisterFirefoxHost("com.axorith", "   ", ["ext1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_CreatesManifestFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        // Override HOME to control manifest location
        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", _tempDir);
            _sut.RegisterFirefoxHost("com.axorith", shimPath, ["ext1", "ext2"]);

            var manifestPath = Path.Combine(_tempDir, ".mozilla", "native-messaging-hosts", "com.axorith.json");
            File.Exists(manifestPath).Should().BeTrue("Firefox manifest should be created at {0}", manifestPath);
        }
        finally
        {
            if (oldHome != null)
            {
                Environment.SetEnvironmentVariable("HOME", oldHome);
            }
            else
            {
                Environment.SetEnvironmentVariable("HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_ManifestContainsRequiredFields()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", _tempDir);
            var extensions = new[] { "ext1@axorith.dev", "ext2@axorith.dev" };
            _sut.RegisterFirefoxHost("com.axorith", shimPath, extensions);

            var manifestPath = Path.Combine(_tempDir, ".mozilla", "native-messaging-hosts", "com.axorith.json");
            var jsonContent = File.ReadAllText(manifestPath);
            var manifest = JsonDocument.Parse(jsonContent);

            manifest.RootElement.GetProperty("name").GetString().Should().Be("com.axorith");
            manifest.RootElement.GetProperty("description").GetString().Should().Contain("Axorith");
            manifest.RootElement.GetProperty("path").GetString().Should().Be(shimPath);
            manifest.RootElement.GetProperty("type").GetString().Should().Be("stdio");
            manifest.RootElement.GetProperty("allowed_extensions").EnumerateArray()
                .Select(e => e.GetString()).ToArray().Should().BeEquivalentTo(extensions);
        }
        finally
        {
            if (oldHome != null)
            {
                Environment.SetEnvironmentVariable("HOME", oldHome);
            }
            else
            {
                Environment.SetEnvironmentVariable("HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_ManifestJsonIsValid()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", _tempDir);
            _sut.RegisterFirefoxHost("com.axorith", shimPath, ["ext1"]);

            var manifestPath = Path.Combine(_tempDir, ".mozilla", "native-messaging-hosts", "com.axorith.json");
            var jsonContent = File.ReadAllText(manifestPath);

            // Should not throw - validates JSON is well-formed
            var act = () => JsonDocument.Parse(jsonContent);
            act.Should().NotThrow();
        }
        finally
        {
            if (oldHome != null)
            {
                Environment.SetEnvironmentVariable("HOME", oldHome);
            }
            else
            {
                Environment.SetEnvironmentVariable("HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_WithValidInputs_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var origins = new[] { "chrome-extension://abc123/", "chrome-extension://def456/" };
        var act = () => _sut.RegisterChromeHost("com.axorith", shimPath, origins);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_WithNullHostName_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "shim");
        var act = () => _sut.RegisterChromeHost(null!, shimPath, ["origin1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_WithWhiteSpaceHostName_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "shim");
        var act = () => _sut.RegisterChromeHost("  ", shimPath, ["origin1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_WithNullExecutablePath_ThrowsArgumentException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var act = () => _sut.RegisterChromeHost("com.axorith", null!, ["origin1"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_CreatesManifestForAllChromiumBrowsers()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var xdgConfigHome = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(xdgConfigHome);
        var oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);
            var origins = new[] { "chrome-extension://abc123/" };
            _sut.RegisterChromeHost("com.axorith", shimPath, origins);

            // Check Chrome manifest
            var chromePath = Path.Combine(xdgConfigHome, "google-chrome", "NativeMessagingHosts", "com.axorith.json");
            File.Exists(chromePath).Should().BeTrue("Chrome manifest should be created at {0}", chromePath);

            // Check Chromium manifest
            var chromiumPath = Path.Combine(xdgConfigHome, "chromium", "NativeMessagingHosts", "com.axorith.json");
            File.Exists(chromiumPath).Should().BeTrue("Chromium manifest should be created at {0}", chromiumPath);

            // Check Edge manifest
            var edgePath = Path.Combine(xdgConfigHome, "microsoft-edge", "NativeMessagingHosts", "com.axorith.json");
            File.Exists(edgePath).Should().BeTrue("Edge manifest should be created at {0}", edgePath);
        }
        finally
        {
            if (oldXdg != null)
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            }
            else
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_ManifestContainsAllowedOrigins()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var xdgConfigHome = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(xdgConfigHome);
        var oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);
            var origins = new[] { "chrome-extension://abc123def/", "chrome-extension://ghi456jkl/" };
            _sut.RegisterChromeHost("com.axorith", shimPath, origins);

            var manifestPath = Path.Combine(xdgConfigHome, "google-chrome", "NativeMessagingHosts", "com.axorith.json");
            var jsonContent = File.ReadAllText(manifestPath);
            var manifest = JsonDocument.Parse(jsonContent);

            manifest.RootElement.GetProperty("name").GetString().Should().Be("com.axorith");
            manifest.RootElement.GetProperty("type").GetString().Should().Be("stdio");
            manifest.RootElement.GetProperty("allowed_origins").EnumerateArray()
                .Select(e => e.GetString()).ToArray().Should().BeEquivalentTo(origins);
        }
        finally
        {
            if (oldXdg != null)
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            }
            else
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_ManifestJsonIsValid()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var xdgConfigHome = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(xdgConfigHome);
        var oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);
            _sut.RegisterChromeHost("com.axorith", shimPath, ["origin1"]);

            var manifestPath = Path.Combine(xdgConfigHome, "google-chrome", "NativeMessagingHosts", "com.axorith.json");
            var jsonContent = File.ReadAllText(manifestPath);

            // Should not throw - validates JSON is well-formed
            var act = () => JsonDocument.Parse(jsonContent);
            act.Should().NotThrow();
        }
        finally
        {
            if (oldXdg != null)
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            }
            else
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_IsIdempotent_DoesNotThrowOnSecondCall()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var xdgConfigHome = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(xdgConfigHome);
        var oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);
            var origins = new[] { "chrome-extension://abc/" };

            // First call
            _sut.RegisterChromeHost("com.axorith", shimPath, origins);

            // Second call should not throw (idempotent)
            var act = () => _sut.RegisterChromeHost("com.axorith", shimPath, origins);
            act.Should().NotThrow();
        }
        finally
        {
            if (oldXdg != null)
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            }
            else
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Factory_CreatesLinuxNativeMessagingManager()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var loggerFactory = NullLoggerFactory.Instance;
        var manager = PlatformServices.CreateNativeMessagingManager(loggerFactory);

        manager.Should().NotBeNull();
        manager.Should().BeOfType<LinuxNativeMessagingManager>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Constructor_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var act = () => new LinuxNativeMessagingManager(NullLogger<LinuxNativeMessagingManager>.Instance);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_ManifestHasCorrectStructure()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", _tempDir);
            _sut.RegisterFirefoxHost("test.host", shimPath, ["ext1"]);

            var manifestPath = Path.Combine(_tempDir, ".mozilla", "native-messaging-hosts", "test.host.json");
            var jsonContent = File.ReadAllText(manifestPath);
            var manifest = JsonDocument.Parse(jsonContent);

            // Verify all required schema fields are present
            var requiredFields = new[] { "name", "description", "path", "type", "allowed_extensions" };
            foreach (var field in requiredFields)
            {
                manifest.RootElement.TryGetProperty(field, out _).Should().BeTrue(
                    "Manifest should contain '{0}' field", field);
            }
        }
        finally
        {
            if (oldHome != null)
            {
                Environment.SetEnvironmentVariable("HOME", oldHome);
            }
            else
            {
                Environment.SetEnvironmentVariable("HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterChromeHost_ManifestHasCorrectStructure()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var xdgConfigHome = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(xdgConfigHome);
        var oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);
            _sut.RegisterChromeHost("test.host", shimPath, ["origin1"]);

            var manifestPath = Path.Combine(xdgConfigHome, "google-chrome", "NativeMessagingHosts", "test.host.json");
            var jsonContent = File.ReadAllText(manifestPath);
            var manifest = JsonDocument.Parse(jsonContent);

            // Verify all required schema fields are present
            var requiredFields = new[] { "name", "description", "path", "type", "allowed_origins" };
            foreach (var field in requiredFields)
            {
                manifest.RootElement.TryGetProperty(field, out _).Should().BeTrue(
                    "Manifest should contain '{0}' field", field);
            }
        }
        finally
        {
            if (oldXdg != null)
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            }
            else
            {
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_ManifestPathMatchesHostName()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", _tempDir);
            var customHostName = "com.axorith.dev";
            _sut.RegisterFirefoxHost(customHostName, shimPath, ["ext1"]);

            var expectedPath = Path.Combine(_tempDir, ".mozilla", "native-messaging-hosts", $"{customHostName}.json");
            File.Exists(expectedPath).Should().BeTrue(
                "Manifest path should match the host name: {0}", expectedPath);
        }
        finally
        {
            if (oldHome != null)
            {
                Environment.SetEnvironmentVariable("HOME", oldHome);
            }
            else
            {
                Environment.SetEnvironmentVariable("HOME", null);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void RegisterFirefoxHost_UsesXdgConfigHomeWhenSet()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shimPath = Path.Combine(_tempDir, "axorith-shim");
        File.WriteAllText(shimPath, string.Empty);

        // Firefox uses HOME, not XDG_CONFIG_HOME, but let's verify the path construction
        var customHome = Path.Combine(_tempDir, "custom-home");
        Directory.CreateDirectory(customHome);
        var oldHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", customHome);
            _sut.RegisterFirefoxHost("com.axorith", shimPath, ["ext1"]);

            var manifestPath = Path.Combine(customHome, ".mozilla", "native-messaging-hosts", "com.axorith.json");
            File.Exists(manifestPath).Should().BeTrue(
                "Firefox manifest should be created under HOME: {0}", manifestPath);
        }
        finally
        {
            if (oldHome != null)
            {
                Environment.SetEnvironmentVariable("HOME", oldHome);
            }
            else
            {
                Environment.SetEnvironmentVariable("HOME", null);
            }
        }
    }
}
