using System.Reflection;
using Axorith.Contracts;
using FluentAssertions;
using Xunit;

namespace Axorith.Client.Tests.Versioning;

/// <summary>
///     Tests for VersionHelper - client version extraction for version handshake.
/// </summary>
public class VersionHelperTests
{
    [Fact]
    public void GetClientVersion_WithExecutingAssembly_ReturnsVersionString()
    {
        // Act
        var version = VersionHelper.GetClientVersion();

        // Assert
        version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetClientVersion_WithContractsAssembly_ReturnsInformationalVersion()
    {
        // Arrange
        var contractsAssembly = typeof(VersionHelper).Assembly;

        // Act
        var version = VersionHelper.GetClientVersion(contractsAssembly);

        // Assert
        version.Should().NotBeNullOrEmpty();
        var expectedVersion = contractsAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (expectedVersion != null)
        {
            version.Should().Be(expectedVersion);
        }
    }

    [Fact]
    public void GetClientVersion_ReturnsInformationalVersionOverAssemblyVersion()
    {
        // Arrange
        var assembly = typeof(VersionHelper).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // Act
        var version = VersionHelper.GetClientVersion(assembly);

        // Assert - informational version takes precedence
        if (informationalVersion != null)
        {
            version.Should().Be(informationalVersion);
        }
        else
        {
            // Falls back to assembly version or "dev"
            version.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void GetClientVersion_ReturnsConsistentValue()
    {
        // Act
        var version1 = VersionHelper.GetClientVersion();
        var version2 = VersionHelper.GetClientVersion();

        // Assert
        version1.Should().Be(version2);
    }

    [Fact]
    public void AuthConstants_HasVersionHeaderName()
    {
        // Assert
        AuthConstants.VersionHeaderName.Should().Be("x-axorith-version");
    }
}
