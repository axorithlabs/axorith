using FluentAssertions;

namespace Axorith.Sdk.Tests;

/// <summary>
///     Tests for Platform enum
/// </summary>
public class PlatformTests
{
    [Fact]
    public void Platform_ShouldHaveThreeValues()
    {
        // Act
        var values = Enum.GetValues<Platform>();

        // Assert
        values.Should().HaveCount(3);
        values.Should().Contain(Platform.Windows);
        values.Should().Contain(Platform.Linux);
        values.Should().Contain(Platform.MacOs);
    }

    [Fact]
    public void Platform_Windows_ShouldHaveCorrectValue()
    {
        // Assert
        ((int)Platform.Windows).Should().Be(0);
    }

    [Fact]
    public void Platform_Linux_ShouldHaveCorrectValue()
    {
        // Assert
        ((int)Platform.Linux).Should().Be(1);
    }

    [Fact]
    public void Platform_MacOs_ShouldHaveCorrectValue()
    {
        // Assert
        ((int)Platform.MacOs).Should().Be(2);
    }

    [Theory]
    [InlineData(Platform.Windows, "Windows")]
    [InlineData(Platform.Linux, "Linux")]
    [InlineData(Platform.MacOs, "MacOs")]
    public void ToString_ShouldReturnEnumName(Platform platform, string expectedName)
    {
        // Assert
        platform.ToString().Should().Be(expectedName);
    }

    [Theory]
    [InlineData(Platform.Windows)]
    [InlineData(Platform.Linux)]
    [InlineData(Platform.MacOs)]
    public void Platform_CanBeUsedInSwitch(Platform platform)
    {
        // Act
        var result = platform switch
        {
            Platform.Windows => "Win",
            Platform.Linux => "Lin",
            Platform.MacOs => "Mac",
            _ => throw new ArgumentOutOfRangeException()
        };

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Platform_CanBeParsed()
    {
        // Act
        var parsedWindows = Enum.Parse<Platform>("Windows");
        var parsedLinux = Enum.Parse<Platform>("Linux");
        var parsedMacOs = Enum.Parse<Platform>("MacOs");

        // Assert
        parsedWindows.Should().Be(Platform.Windows);
        parsedLinux.Should().Be(Platform.Linux);
        parsedMacOs.Should().Be(Platform.MacOs);
    }

    [Fact]
    public void Platform_TryParse_WithInvalidValue_ShouldReturnFalse()
    {
        // Act
        var result = Enum.TryParse<Platform>("iOS", out var platform);

        // Assert
        result.Should().BeFalse();
        platform.Should().Be(default);
    }

    [Fact]
    public void Platform_IsDefined_ShouldReturnTrueForValidValues()
    {
        // Assert
        Enum.IsDefined(typeof(Platform), Platform.Windows).Should().BeTrue();
        Enum.IsDefined(typeof(Platform), Platform.Linux).Should().BeTrue();
        Enum.IsDefined(typeof(Platform), Platform.MacOs).Should().BeTrue();
    }

    [Fact]
    public void Platform_IsDefined_ShouldReturnFalseForInvalidValue()
    {
        // Assert
        Enum.IsDefined(typeof(Platform), 999).Should().BeFalse();
    }

    [Fact]
    public void Platform_CanBeUsedInCollections()
    {
        // Arrange
        var platforms = new List<Platform>
        {
            Platform.Windows,
            Platform.Linux,
            Platform.MacOs
        };

        // Assert
        platforms.Should().HaveCount(3);
        platforms.Should().ContainInOrder(Platform.Windows, Platform.Linux, Platform.MacOs);
    }

    [Fact]
    public void Platform_CanBeUsedAsDictionaryKey()
    {
        // Arrange
        var dictionary = new Dictionary<Platform, string>
        {
            { Platform.Windows, "win32" },
            { Platform.Linux, "linux" },
            { Platform.MacOs, "darwin" }
        };

        // Assert
        dictionary[Platform.Windows].Should().Be("win32");
        dictionary[Platform.Linux].Should().Be("linux");
        dictionary[Platform.MacOs].Should().Be("darwin");
    }

    [Fact]
    public void Platform_GetNames_ShouldReturnAllNames()
    {
        // Act
        var names = Enum.GetNames<Platform>();

        // Assert
        names.Should().Contain("Windows");
        names.Should().Contain("Linux");
        names.Should().Contain("MacOs");
        names.Should().HaveCount(3);
    }

    [Fact]
    public void Platform_CanBeUsedInHashSet()
    {
        // Arrange
        var supportedPlatforms = new HashSet<Platform>
        {
            Platform.Windows,
            Platform.Linux
        };

        // Assert
        supportedPlatforms.Should().Contain(Platform.Windows);
        supportedPlatforms.Should().Contain(Platform.Linux);
        supportedPlatforms.Should().NotContain(Platform.MacOs);
    }

    [Fact]
    public void Platform_CanBeUsedInLinqQueries()
    {
        // Arrange
        var allPlatforms = Enum.GetValues<Platform>();

        // Act
        var nonWindowsPlatforms = allPlatforms.Where(p => p != Platform.Windows).ToList();

        // Assert
        nonWindowsPlatforms.Should().HaveCount(2);
        nonWindowsPlatforms.Should().Contain(Platform.Linux);
        nonWindowsPlatforms.Should().Contain(Platform.MacOs);
        nonWindowsPlatforms.Should().NotContain(Platform.Windows);
    }

    [Fact]
    public void Platform_Array_CanBeUsedInModuleDefinition()
    {
        // Arrange
        var platforms = new[] { Platform.Windows, Platform.Linux };

        // Act
        var definition = new ModuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Cross-Platform Module",
            Platforms = platforms
        };

        // Assert
        definition.Platforms.Should().ContainInOrder(Platform.Windows, Platform.Linux);
    }

    [Theory]
    [InlineData(Platform.Windows, Platform.Windows, true)]
    [InlineData(Platform.Windows, Platform.Linux, false)]
    [InlineData(Platform.Linux, Platform.MacOs, false)]
    public void Platform_Equality_ShouldWorkCorrectly(Platform p1, Platform p2, bool shouldBeEqual)
    {
        // Assert
        (p1 == p2).Should().Be(shouldBeEqual);
        p1.Equals(p2).Should().Be(shouldBeEqual);
    }

    [Fact]
    public void Platform_CanBeCastToInt()
    {
        // Act
        var windows = (int)Platform.Windows;
        var linux = (int)Platform.Linux;
        var macos = (int)Platform.MacOs;

        // Assert
        windows.Should().Be(0);
        linux.Should().Be(1);
        macos.Should().Be(2);
    }

    [Fact]
    public void Platform_CanBeCastFromInt()
    {
        // Act
        var windows = (Platform)0;
        var linux = (Platform)1;
        var macos = (Platform)2;

        // Assert
        windows.Should().Be(Platform.Windows);
        linux.Should().Be(Platform.Linux);
        macos.Should().Be(Platform.MacOs);
    }
}