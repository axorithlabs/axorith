using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Services;

public class SessionManagerNegativeTests : IAsyncLifetime
{
    private readonly Mock<IModuleRegistry> _mockRegistry;
    private readonly SessionManager _sessionManager;

    public SessionManagerNegativeTests()
    {
        _mockRegistry = new Mock<IModuleRegistry>();
        _sessionManager = SessionManagerTestFactory.CreateManager(_mockRegistry.Object);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _sessionManager.DisposeAsync().AsTask();

    [Fact]
    public Task StartSessionAsync_WithValidationFailure_ShouldRollbackAndStop() =>
        AssertStartFailsAsync(module => module.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Fail("Validation failed")));

    [Fact]
    public Task StartSessionAsync_WithOnSessionStartException_ShouldRollback() =>
        AssertStartFailsAsync(module => module.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Module start failed")));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModuleTimeoutCancelsThePendingOperationAndRollsBack(bool duringValidation)
    {
        var module = SessionManagerTestFactory.CreateModule();
        var entered = false;
        var cancelled = false;
        async Task WaitForCancellation(CancellationToken token)
        {
            entered = true;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled = token.IsCancellationRequested; }
        }
        if (duringValidation)
            module.Setup(value => value.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    await WaitForCancellation(token);
                    return ValidationResult.Success;
                });
        else
            module.Setup(value => value.OnSessionStartAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => WaitForCancellation(token));
        var preset = SessionManagerTestFactory.CreatePreset(SessionManagerTestFactory.Register(_mockRegistry, module));
        await using var manager = new SessionManager(_mockRegistry.Object, NullLogger<SessionManager>.Instance,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1),
            NoopTelemetryService.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<SessionException>(() => manager.StartSessionAsync(preset, deadline.Token))
            .WaitAsync(TimeSpan.FromSeconds(6));

        error.InnerException.Should().BeOfType<TimeoutException>();
        entered.Should().BeTrue();
        cancelled.Should().BeTrue();
        deadline.IsCancellationRequested.Should().BeFalse("the module deadline must fire before the test deadline");
        manager.IsSessionRunning.Should().BeFalse();
        manager.ActiveSession.Should().BeNull();
        module.Verify(value => value.OnSessionEndAsync(It.IsAny<CancellationToken>()),
            duringValidation ? Times.Never() : Times.Once());
        module.Verify(value => value.Dispose(), Times.Once());
    }

    [Fact]
    public async Task StartSessionAsync_WithMultipleModules_OneFailsValidation_ShouldRollback()
    {
        var idGood = Guid.NewGuid();
        var idBad = Guid.NewGuid();

        var goodModule = SessionManagerTestFactory.CreateModule();
        var badModule = SessionManagerTestFactory.CreateModule();

        badModule.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Fail("Bad module validation failed"));
        SessionManagerTestFactory.Register(_mockRegistry, goodModule, idGood);
        SessionManagerTestFactory.Register(_mockRegistry, badModule, idBad);

        var preset = SessionManagerTestFactory.CreatePreset(idGood, idBad);

        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithMultipleModules_OneFailsStart_ShouldRollbackAndStopStartedOnes()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var module1 = SessionManagerTestFactory.CreateModule();
        var module2 = SessionManagerTestFactory.CreateModule();

        var module1Stopped = false;
        module1.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module1Stopped = true)
            .Returns(Task.CompletedTask);

        module2.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Module 2 failed"));
        SessionManagerTestFactory.Register(_mockRegistry, module1, id1);
        SessionManagerTestFactory.Register(_mockRegistry, module2, id2);

        var preset = SessionManagerTestFactory.CreatePreset(id1, id2);

        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        module1Stopped.Should().BeTrue("Module 1 should have been stopped during rollback");
        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopCurrentSessionAsync_WithModuleException_ShouldStillStopOthers()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var module1 = SessionManagerTestFactory.CreateModule();
        var module2 = SessionManagerTestFactory.CreateModule();

        var module2Stopped = false;

        module1.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Module 1 stop failed"));

        module2.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module2Stopped = true)
            .Returns(Task.CompletedTask);
        SessionManagerTestFactory.Register(_mockRegistry, module1, id1);
        SessionManagerTestFactory.Register(_mockRegistry, module2, id2);

        var preset = SessionManagerTestFactory.CreatePreset(id1, id2);

        await _sessionManager.StartSessionAsync(preset);

        var stoppedTcs = new TaskCompletionSource();
        _sessionManager.SessionStopped += _ => stoppedTcs.TrySetResult();
        await _sessionManager.StopCurrentSessionAsync();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        module2Stopped.Should().BeTrue("Module 2 should still be stopped despite Module 1 failure");
        _sessionManager.IsSessionRunning.Should().BeFalse();
    }
    private async Task AssertStartFailsAsync(Action<Mock<IModule>> configure)
    {
        var module = SessionManagerTestFactory.CreateModule();
        configure(module);
        var preset = SessionManagerTestFactory.CreatePreset(SessionManagerTestFactory.Register(_mockRegistry, module));

        await _sessionManager.Invoking(manager => manager.StartSessionAsync(preset))
            .Should().ThrowAsync<SessionException>();
        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

}
