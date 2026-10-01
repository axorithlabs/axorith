using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Sdk.Tests.Actions;

public sealed class ActionAsyncTests
{
    [Fact]
    public async Task InvokeAsyncEmitsOnlyWhenEnabled()
    {
        using var action = Action.Create("login", "Login");
        var invoked = 0;
        using var subscription = action.Invoked.Subscribe(_ => invoked++);

        await action.InvokeAsync();
        action.SetEnabled(false);
        await action.InvokeAsync();

        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task InvokeAsyncWaitsUntilTheRegisteredHandlerCompletes()
    {
        using var action = Action.Create("login", "Login");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        action.OnInvokeAsync(async () =>
        {
            started.SetResult();
            await release.Task;
            completed = true;
        });

        var invocation = action.InvokeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(invocation.IsCompleted);
        release.SetResult();
        await invocation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(completed);
    }

    [Fact]
    public async Task ReRegisteringHandlerUsesTheLatestHandler()
    {
        using var action = Action.Create("login", "Login");
        var oldHandlerCalled = false;
        var latestHandlerCalled = false;
        action.OnInvokeAsync(() =>
        {
            oldHandlerCalled = true;
            return Task.CompletedTask;
        });
        action.OnInvokeAsync(() =>
        {
            latestHandlerCalled = true;
            return Task.CompletedTask;
        });

        await action.InvokeAsync();

        Assert.False(oldHandlerCalled);
        Assert.True(latestHandlerCalled);
    }

    [Fact]
    public async Task HandlerExceptionsReachTheCaller()
    {
        using var action = Action.Create("login", "Login");
        action.OnInvokeAsync(() => Task.FromException(new InvalidOperationException("login failed")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => action.InvokeAsync());

        Assert.Equal("login failed", exception.Message);
    }

    [Fact]
    public async Task ReplacingHandlerDuringAnInvocationDoesNotReplaceItsRunningCall()
    {
        using var action = Action.Create("login", "Login");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCompleted = false;
        var secondCalled = false;
        action.OnInvokeAsync(async () =>
        {
            started.SetResult();
            await release.Task;
            firstCompleted = true;
        });

        var invocation = action.InvokeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        action.OnInvokeAsync(() =>
        {
            secondCalled = true;
            return Task.CompletedTask;
        });
        release.SetResult();
        await invocation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(firstCompleted);
        Assert.False(secondCalled);
    }
}
