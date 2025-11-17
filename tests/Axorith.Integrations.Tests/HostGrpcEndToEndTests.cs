using Axorith.Contracts;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class HostTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var presetsPath = Path.Combine(Path.GetTempPath(), "AxorithTests", "Presets");

            Directory.CreateDirectory(presetsPath);

            var testConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:PresetsPath"] = presetsPath
                })
                .Build();

            configBuilder.AddConfiguration(testConfig);
        });
    }
}

public class HostGrpcEndToEndTests(HostTestFactory factory) : IClassFixture<HostTestFactory>
{
    static HostGrpcEndToEndTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    private (DiagnosticsService.DiagnosticsServiceClient diagnostics,
        PresetsService.PresetsServiceClient presets,
        SessionsService.SessionsServiceClient sessions,
        ModulesService.ModulesServiceClient modules,
        GrpcChannel channel) CreateClients()
    {
        var httpClient = factory.CreateDefaultClient();

        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);
        var presets = new PresetsService.PresetsServiceClient(channel);
        var sessions = new SessionsService.SessionsServiceClient(channel);
        var modules = new ModulesService.ModulesServiceClient(channel);

        return (diagnostics, presets, sessions, modules, channel);
    }

    [Fact]
    public async Task Diagnostics_GetHealth_ShouldReturnHealthy()
    {
        var (diagnostics, _, _, _, channel) = CreateClients();

        using (channel)
        {
            var response = await diagnostics.GetHealthAsync(new HealthCheckRequest());

            response.Should().NotBeNull();
            response.Status.Should().Be(HealthStatus.Healthy);
        }
    }

    [Fact]
    public async Task Presets_Create_List_Get_Delete_ShouldRoundTrip()
    {
        var (_, presets, _, _, channel) = CreateClients();

        using (channel)
        {
            var name = $"IntegrationTest-{Guid.NewGuid():N}";

            var created = await presets.CreatePresetAsync(new CreatePresetRequest
            {
                Preset = new Preset
                {
                    Name = name
                }
            });

            created.Should().NotBeNull();
            created.Name.Should().Be(name);
            Guid.TryParse(created.Id, out _).Should().BeTrue();

            var list = await presets.ListPresetsAsync(new ListPresetsRequest());
            list.Presets.Should().NotBeNull();
            list.Presets.Should().Contain(p => p.Id == created.Id);

            var fetched = await presets.GetPresetAsync(new GetPresetRequest
            {
                PresetId = created.Id
            });

            fetched.Should().NotBeNull();
            fetched.Id.Should().Be(created.Id);
            fetched.Name.Should().Be(name);

            await presets.DeletePresetAsync(new DeletePresetRequest
            {
                PresetId = created.Id
            });

            var afterDelete = await presets.ListPresetsAsync(new ListPresetsRequest());
            afterDelete.Presets.Should().NotContain(p => p.Id == created.Id);
        }
    }

    [Fact]
    public async Task Presets_GetPreset_WithInvalidId_ShouldReturnRpcInvalidArgument()
    {
        var (_, presets, _, _, channel) = CreateClients();

        using (channel)
        {
            var act = async () =>
                await presets.GetPresetAsync(new GetPresetRequest
                {
                    PresetId = "invalid-guid"
                });

            var exception = await Assert.ThrowsAsync<RpcException>(act);
            exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        }
    }

    [Fact]
    public async Task Sessions_GetSessionState_WhenNoActiveSession_ShouldReturnInactive()
    {
        var (_, _, sessions, _, channel) = CreateClients();

        using (channel)
        {
            var state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());

            state.Should().NotBeNull();
            state.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Sessions_StartSession_WithInvalidPresetId_ShouldReturnFailure()
    {
        var (_, _, sessions, _, channel) = CreateClients();

        using (channel)
        {
            var response = await sessions.StartSessionAsync(new StartSessionRequest
            {
                PresetId = "invalid-guid"
            });

            response.Should().NotBeNull();
            response.Success.Should().BeFalse();
            response.Message.Should().Contain("Invalid preset ID");
        }
    }

    [Fact]
    public async Task Sessions_StartSession_WithUnknownPreset_ShouldReturnFailure()
    {
        var (_, _, sessions, _, channel) = CreateClients();

        using (channel)
        {
            var presetId = Guid.NewGuid().ToString();

            var response = await sessions.StartSessionAsync(new StartSessionRequest
            {
                PresetId = presetId
            });

            response.Should().NotBeNull();
            response.Success.Should().BeFalse();
            response.Message.Should().Contain("Preset not found");
        }
    }

    [Fact]
    public async Task Modules_ListModules_ShouldReturnWithoutError()
    {
        var (_, _, _, modules, channel) = CreateClients();

        using (channel)
        {
            var response = await modules.ListModulesAsync(new ListModulesRequest());

            response.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Modules_ListModules_WithCategoryFilter_ShouldReturnOnlyThatCategory()
    {
        var (_, _, _, modules, channel) = CreateClients();

        using (channel)
        {
            var systemResponse = await modules.ListModulesAsync(new ListModulesRequest
            {
                Category = "System"
            });

            systemResponse.Should().NotBeNull();
            systemResponse.Modules.Should().NotBeEmpty();
            systemResponse.Modules.Should().OnlyContain(m => m.Category == "System");

            var musicResponse = await modules.ListModulesAsync(new ListModulesRequest
            {
                Category = "music"
            });

            musicResponse.Should().NotBeNull();
            musicResponse.Modules.Should().NotBeEmpty();
            musicResponse.Modules.Should().OnlyContain(m => m.Category == "Music");

            var unknownResponse = await modules.ListModulesAsync(new ListModulesRequest
            {
                Category = "DoesNotExist"
            });

            unknownResponse.Modules.Should().BeEmpty();
        }
    }
}