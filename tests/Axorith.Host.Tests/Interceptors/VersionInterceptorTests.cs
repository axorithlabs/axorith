using Axorith.Contracts;
using Axorith.Host.Interceptors;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Host.Tests.Interceptors;

/// <summary>
///     Tests for VersionInterceptor - server-side version validation.
/// </summary>
public class VersionInterceptorTests
{
    private readonly VersionInterceptor _interceptor;

    public VersionInterceptorTests()
    {
        _interceptor = new VersionInterceptor(NullLogger<VersionInterceptor>.Instance);
    }

    private static ServerCallContext CreateTestContext(Metadata? headers = null)
    {
        return TestServerCallContext.Create(
            method: "test",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: headers ?? [],
            cancellationToken: CancellationToken.None,
            peer: "127.0.0.1",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => new WriteOptions(),
            writeOptionsSetter: _ => { }
        );
    }

    #region Version Parsing Tests

    [Theory]
    [InlineData("1.2.0", "1.3.0", true)]   // Same major, host minor >= client minor
    [InlineData("1.2.0", "2.0.0", false)]   // Different major
    [InlineData("1.3.0", "1.2.0", false)]   // Host minor < client minor
    [InlineData("1.0.0", "1.0.0", true)]    // Exact match
    [InlineData("1.2.0", "1.2.5", true)]    // Same major, host minor > client minor
    [InlineData("2.0.0", "1.0.0", false)]   // Client major > host major
    public void IsCompatible_ShouldValidateCorrectly(string clientVersion, string hostVersion, bool expected)
    {
        // Act
        var result = VersionInterceptor.IsCompatible(clientVersion, hostVersion);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.2.0-dev", "1.3.0", true)]    // Dev suffix
    [InlineData("1.2.0+build", "1.3.0", true)]   // Build metadata
    [InlineData("1.2.0-alpha.1", "1.3.0", true)] // Pre-release with dots
    public void IsCompatible_ShouldHandleVersionSuffixes(string clientVersion, string hostVersion, bool expected)
    {
        // Act
        var result = VersionInterceptor.IsCompatible(clientVersion, hostVersion);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "1.0.0")]           // Empty client version
    [InlineData("invalid", "1.0.0")]    // Non-numeric client version
    [InlineData("1", "1.0.0")]          // Single number
    public void IsCompatible_WithInvalidClientVersion_ReturnsFalse(string clientVersion, string hostVersion)
    {
        // Act
        var result = VersionInterceptor.IsCompatible(clientVersion, hostVersion);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsCompatible_WithEmptyHostVersion_ReturnsTrue()
    {
        // Act
        var result = VersionInterceptor.IsCompatible("1.0.0", "");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Interceptor Validation Tests

    [Fact]
    public void ValidateVersion_WithMissingHeader_ThrowsInvalidArgument()
    {
        // Arrange
        var context = CreateTestContext();

        // Act & Assert
        var act = () => _interceptor.UnaryServerHandler(
            new TestRequest(),
            context,
            (req, ctx) => Task.FromResult(new TestResponse()));

        act.Should().ThrowAsync<RpcException>()
            .WithMessage("*Missing required*")
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    [Fact]
    public void ValidateVersion_WithDevBuild_Succeeds()
    {
        // Arrange
        var headers = new Metadata { { AuthConstants.VersionHeaderName, "dev" } };
        var context = CreateTestContext(headers);

        // Act & Assert
        var act = () => _interceptor.UnaryServerHandler(
            new TestRequest(),
            context,
            (req, ctx) => Task.FromResult(new TestResponse()));

        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ValidateVersion_WithCompatibleVersion_Succeeds()
    {
        // Arrange
        var headers = new Metadata { { AuthConstants.VersionHeaderName, "1.0.0" } };
        var context = CreateTestContext(headers);

        // Act & Assert
        var act = () => _interceptor.UnaryServerHandler(
            new TestRequest(),
            context,
            (req, ctx) => Task.FromResult(new TestResponse()));

        act.Should().NotThrowAsync();
    }

    [Fact]
    public void CurrentHostVersion_ReturnsNonEmptyString()
    {
        // Act
        var version = VersionInterceptor.CurrentHostVersion;

        // Assert
        version.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Test Helpers

    private class TestRequest;
    private class TestResponse;

    #endregion
}
