using System.Collections.Concurrent;
using System.Text.Json;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
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

    [Fact]
    public async Task SessionStartAndEndSendAuthenticatedServiceCalls()
    {
        var calls = new ConcurrentQueue<ServiceCall>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapPost("/api/services/{domain}/{service}", async context =>
        {
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
                   startEntity: string.Empty, endEntity: "script.focus_complete"))
        {
            await module.OnSessionEndAsync(CancellationToken.None);
        }

        calls.Should().Equal(
            new ServiceCall("/api/services/homeassistant/turn_on", "Bearer integration-token", "light.desk"),
            new ServiceCall("/api/services/homeassistant/turn_off", "Bearer integration-token", "light.desk"),
            new ServiceCall("/api/services/homeassistant/turn_on", "Bearer integration-token", "script.focus_complete"));
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

    private sealed class EmptySecureStorage : ISecureStorageService
    {
        public void StoreSecret(string key, string secret) { }
        public string? RetrieveSecret(string key) => null;
        public void DeleteSecret(string key) { }
    }

    private sealed class TestModuleLogger : IModuleLogger
    {
        public void LogDebug(string messageTemplate, params object[] args) { }
        public void LogInfo(string messageTemplate, params object[] args) { }
        public void LogWarning(string messageTemplate, params object[] args) { }
        public void LogError(Exception? exception, string messageTemplate, params object[] args) { }
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) { }
    }
}
