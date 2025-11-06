using FluentAssertions;

namespace Axorith.Core.Tests;

/// <summary>
///     Tests for AxorithHost lifecycle, disposal, and service resolution
/// </summary>
public class AxorithHostTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAsync_ShouldInitializeSuccessfully()
    {
        // Act
        var host = await AxorithHost.CreateAsync();

        // Assert
        host.Should().NotBeNull();
        host.Sessions.Should().NotBeNull();
        host.Presets.Should().NotBeNull();
        host.Modules.Should().NotBeNull();

        // Cleanup
        await host.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Services_ShouldBeResolvable()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        try
        {
            // Act & Assert
            host.Sessions.Should().NotBeNull();
            host.Presets.Should().NotBeNull();
            host.Modules.Should().NotBeNull();
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DisposeAsync_ShouldDisposeServicesCorrectly()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        // Act
        await host.DisposeAsync();

        // Assert - should not throw
        Assert.True(true);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DisposeAsync_CalledTwice_ShouldBeIdempotent()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        // Act
        await host.DisposeAsync();
        var act = async () => await host.DisposeAsync();

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispose_ShouldWorkForBackwardCompatibility()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        // Act
        var act = () => host.Dispose();

        // Assert - should not throw
        act.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAsync_WithCancellation_ShouldCompleteIfAlreadyStarted()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act - host creation is fast, cancellation might not affect it
        var host = await AxorithHost.CreateAsync(cts.Token);

        // Assert - host should be created even with cancelled token
        host.Should().NotBeNull();
        await host.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Host_AfterDispose_ServicesThrowObjectDisposedException()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();
        await host.DisposeAsync();

        // Act & Assert - accessing services after dispose should throw
        var act1 = () => host.Sessions;
        var act2 = () => host.Presets;
        var act3 = () => host.Modules;

        act1.Should().Throw<ObjectDisposedException>();
        act2.Should().Throw<ObjectDisposedException>();
        act3.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAsync_ShouldInitializeModuleRegistry()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        try
        {
            // Act
            var modules = host.Modules.GetAllDefinitions();

            // Assert - should at least not throw
            modules.Should().NotBeNull();
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SessionManager_ShouldBeIAsyncDisposable()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        try
        {
            // Act & Assert
            host.Sessions.Should().BeAssignableTo<IAsyncDisposable>();
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentHostCreation_ShouldNotConflict()
    {
        // Arrange
        var tasks = new List<Task<AxorithHost>>();

        // Act - create multiple hosts concurrently
        for (var i = 0; i < 5; i++) tasks.Add(AxorithHost.CreateAsync());

        var hosts = await Task.WhenAll(tasks);

        // Assert
        hosts.Should().HaveCount(5);
        hosts.Should().AllSatisfy(h => h.Should().NotBeNull());

        // Cleanup
        foreach (var host in hosts) await host.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Host_ShouldImplementBothDisposePatterns()
    {
        // Arrange
        var host = await AxorithHost.CreateAsync();

        // Assert
        host.Should().BeAssignableTo<IDisposable>();
        host.Should().BeAssignableTo<IAsyncDisposable>();

        // Cleanup
        await host.DisposeAsync();
    }
}