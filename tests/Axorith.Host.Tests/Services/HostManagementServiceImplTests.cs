using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

public class HostManagementServiceImplTests
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<IHostApplicationLifetime> _mockLifetime;
    private readonly HostManagementServiceImpl _service;

    public HostManagementServiceImplTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();
        _mockLifetime = new Mock<IHostApplicationLifetime>();

        _service = new HostManagementServiceImpl(
            _mockSessionManager.Object,
            _mockLifetime.Object,
            NullLogger<HostManagementServiceImpl>.Instance
        );
    }

    private static ServerCallContext CreateTestContext()
    {
        return TestServerCallContext.Create(
            method: "TestMethod",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: [],
            cancellationToken: CancellationToken.None,
            peer: "127.0.0.1",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => new WriteOptions(),
            writeOptionsSetter: _ => { }
        );
    }

    #region RequestShutdown Tests

    [Fact]
    public async Task RequestShutdown_WhenExceptionOccurs_ShouldReturnFailure()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.IsSessionRunning).Throws(new InvalidOperationException("Test exception"));

        var request = new ShutdownRequest { Reason = "User requested" };
        var context = CreateTestContext();

        // Act
        var response = await _service.RequestShutdown(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Accepted.Should().BeFalse();
        response.Message.Should().Contain("Shutdown failed");
    }

    #endregion

    #region GetStatus Tests

    #endregion
}
