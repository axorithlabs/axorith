using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Axorith.Integrations.Tests;

using HomeAssistantModule = Axorith.Module.HomeAssistant.Module;

public sealed class HomeAssistantModuleTests
{
    [Fact]
    public async Task TestConnectionActionSendsAuthenticatedRequestAndReportsSuccess()
    {
        var authorization = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapGet("/api/", context =>
        {
            authorization.Enqueue(context.Request.Headers["Authorization"].ToString());
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var module = CreateModule(new TestHttpClientFactory(client), client.BaseAddress!.ToString(),
            startEntity: string.Empty, endEntity: string.Empty);
        await module.InitializeAsync(CancellationToken.None);

        var action = module.GetActions().Single();
        Assert.Equal("BaseUrl", action.SettingKey);
        await action.InvokeAsync();

        Assert.Equal("Bearer integration-token", Assert.Single(authorization));
        Assert.Equal("Connected OK", action.GetCurrentLabel());
        Assert.True(action.GetCurrentEnabled());
    }

    [Theory]
    [InlineData("script.focus_complete", "turn_on")]
    [InlineData("scene.relax", "turn_on")]
    [InlineData("automation.focus_complete", "turn_on")]
    [InlineData("input_button.focus_complete", "turn_on")]
    [InlineData("LIGHT.desk", "turn_off")]
    public async Task SessionStartAndEndSendAuthenticatedServiceCalls(string endEntity, string endService)
    {
        var calls = new ConcurrentQueue<ServiceCall>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapPost("/api/services/{domain}/{service}", async context =>
        {
            Assert.Equal("application/json; charset=utf-8", context.Request.ContentType);
            using var reader = new StreamReader(context.Request.Body);
            using var body = JsonDocument.Parse(await reader.ReadToEndAsync());
            calls.Enqueue(new ServiceCall(context.Request.Path.Value!,
                context.Request.Headers["Authorization"].ToString(),
                body.RootElement.GetProperty("entity_id").GetString()!));
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("[]");
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        var clientFactory = new TestHttpClientFactory(client);

        using (var module = CreateModule(clientFactory, client.BaseAddress!.ToString(),
                   startEntity: "light.desk", endEntity: "light.desk"))
        {
            (await module.ValidateSettingsAsync(CancellationToken.None)).Status.Should().Be(Axorith.Sdk.ValidationStatus.Ok);
            await module.OnSessionStartAsync(CancellationToken.None);
            await module.OnSessionEndAsync(CancellationToken.None);
        }

        using (var module = CreateModule(clientFactory, client.BaseAddress!.ToString(),
                   startEntity: string.Empty, endEntity: endEntity))
        {
            await module.OnSessionEndAsync(CancellationToken.None);
        }

        calls.Should().Equal(
            new ServiceCall("/api/services/homeassistant/turn_on", "Bearer integration-token", "light.desk"),
            new ServiceCall("/api/services/homeassistant/turn_off", "Bearer integration-token", "light.desk"),
            new ServiceCall("/api/services/homeassistant/" + endService, "Bearer integration-token", endEntity));
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(503, false)]
    [InlineData(401, true)]
    [InlineData(503, true)]
    public async Task RejectedServiceCallPropagatesTheStatusAndDoesNotRetryOrReportSuccess(int status, bool ending)
    {
        var calls = new ConcurrentQueue<ServiceCall>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapPost("/api/services/{domain}/{service}", async context =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            calls.Enqueue(new ServiceCall(context.Request.Path.Value!,
                context.Request.Headers.Authorization.ToString(), body.RootElement.GetProperty("entity_id").GetString()!));
            context.Response.StatusCode = status;
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var module = CreateModule(new TestHttpClientFactory(client), client.BaseAddress!.ToString(),
            "light.desk", "light.desk");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => ending
            ? module.OnSessionEndAsync(CancellationToken.None)
            : module.OnSessionStartAsync(CancellationToken.None));

        Assert.Equal((System.Net.HttpStatusCode)status, error.StatusCode);
        Assert.Equal(new ServiceCall("/api/services/homeassistant/" + (ending ? "turn_off" : "turn_on"),
            "Bearer integration-token", "light.desk"), Assert.Single(calls));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task ConnectionActionReportsARejectedRequestAndCanRecoverOnTheSameModule(int status)
    {
        var authorization = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapGet("/api/", context =>
        {
            authorization.Enqueue(context.Request.Headers.Authorization.ToString());
            context.Response.StatusCode = authorization.Count == 1 ? status : StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var module = CreateModule(new TestHttpClientFactory(client), client.BaseAddress!.ToString(), "", "");
        await module.InitializeAsync(CancellationToken.None);
        var action = Assert.Single(module.GetActions());

        await action.InvokeAsync();
        Assert.Equal("Connection Failed", action.GetCurrentLabel());
        Assert.True(action.GetCurrentEnabled());
        Assert.Single(authorization);

        await action.InvokeAsync();
        Assert.Equal("Connected OK", action.GetCurrentLabel());
        Assert.True(action.GetCurrentEnabled());
        Assert.Equal(new[] { "Bearer integration-token", "Bearer integration-token" }, authorization.ToArray());
    }

    [Fact]
    public async Task HttpDeadlineCancelsAPendingServiceCallAtTheServer()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapPost("/api/services/homeassistant/turn_on", async context =>
        {
            using var subscription = context.RequestAborted.Register(() => canceled.TrySetResult());
            received.TrySetResult();
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.Timeout = TimeSpan.FromMilliseconds(500);
        using var module = CreateModule(new TestHttpClientFactory(client), client.BaseAddress!.ToString(), "light.desk", "");

        var operation = module.OnSessionStartAsync(CancellationToken.None);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static HomeAssistantModule CreateModule(IHttpClientFactory clientFactory, string baseUrl, string startEntity,
        string endEntity)
    {
        var module = new HomeAssistantModule(new TestModuleLogger(), clientFactory, new EmptySecureStorage());
        Set(module, "BaseUrl", baseUrl);
        Set(module, "AccessToken", "integration-token");
        Set(module, "StartEntityId", startEntity);
        Set(module, "EndEntityId", endEntity);
        return module;
    }

    private static void Set(HomeAssistantModule module, string key, string value) =>
        module.GetSettings().Single(setting => setting.Key == key).SetValueFromString(value);

    private sealed record ServiceCall(string Path, string Authorization, string EntityId);

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

}
