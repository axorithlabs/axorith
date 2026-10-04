using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
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

    [Fact]
    public async Task RequestShutdown_WhenExceptionOccurs_ShouldReturnFailure()
    {
        _mockSessionManager.Setup(m => m.IsSessionRunning).Throws(new InvalidOperationException("Test exception"));

        var request = new ShutdownRequest { Reason = "User requested" };
        var context = GrpcTestContext.Create();

        var response = await _service.RequestShutdown(request, context);

        response.Accepted.Should().BeFalse();
        response.Message.Should().Contain("Shutdown failed");
    }



}
