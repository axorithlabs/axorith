using Autofac;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Integrations.Tests;

public sealed class HostTestFactory : WebApplicationFactory<Program>
{
    public string TestDataPath { get; }

    public HostTestFactory()
    {
        TestDataPath = Path.Combine(Path.GetTempPath(), "AxorithTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestDataPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var testConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:PresetsPath"] = Path.Combine(TestDataPath, "presets"),
                    ["Persistence:LogsPath"] = Path.Combine(TestDataPath, "logs"),
                    ["Modules:SearchPaths:0"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:1"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:2"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:3"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:4"] = Path.Combine(TestDataPath, "empty_modules")
                })
                .Build();

            configBuilder.AddConfiguration(testConfig);
        });

        builder.ConfigureTestContainer<ContainerBuilder>(containerBuilder =>
        {
            var mockRegistry = new Mock<IModuleRegistry>();

            var testModules = new List<ModuleDefinition>
            {
                new()
                {
                    Id = Guid.NewGuid(), Name = "System Module", Category = "System", Platforms = [Platform.Windows]
                },
                new()
                {
                    Id = Guid.NewGuid(), Name = "Music Module", Category = "Music", Platforms = [Platform.Windows]
                },
                new()
                {
                    Id = Guid.NewGuid(), Name = "Dev Module", Category = "Development", Platforms = [Platform.Windows]
                }
            };

            mockRegistry.Setup(r => r.GetAllDefinitions()).Returns(testModules);

            mockRegistry.Setup(r => r.GetDefinitionById(It.IsAny<Guid>()))
                .Returns((Guid id) => testModules.FirstOrDefault(m => m.Id == id));

            // Регистрируем мок. Благодаря PreserveExistingDefaults в Program.cs, эта регистрация выиграет.
            containerBuilder.RegisterInstance(mockRegistry.Object)
                .As<IModuleRegistry>()
                .SingleInstance();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(TestDataPath))
            {
                Directory.Delete(TestDataPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

public class HostGrpcEndToEndTests(HostTestFactory factory) : IClassFixture<HostTestFactory>
{
    static HostGrpcEndToEndTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    private async Task<(
        DiagnosticsService.DiagnosticsServiceClient diagnostics,
        PresetsService.PresetsServiceClient presets,
        SessionsService.SessionsServiceClient sessions,
        ModulesService.ModulesServiceClient modules,
        GrpcChannel channel)> CreateAuthenticatedClientsAsync()
    {
        var httpClient = factory.CreateDefaultClient();

        var tokenPath = Path.Combine(factory.TestDataPath, ".auth_token");

        var token = string.Empty;
        for (var i = 0; i < 50; i++)
        {
            if (File.Exists(tokenPath))
            {
                try
                {
                    using var fs = new FileStream(tokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    token = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        break;
                    }
                }
                catch
                {
                    /* ignore and retry */
                }
            }

            await Task.Delay(100);
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new FileNotFoundException(
                $"Auth token not found at {tokenPath}. Host failed to start or write token.");
        }

        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            metadata.Add("x-axorith-auth-token", token);
            return Task.CompletedTask;
        });

        var channelOptions = new GrpcChannelOptions
        {
            HttpClient = httpClient,
            Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials),
            UnsafeUseInsecureChannelCallCredentials = true
        };

        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, channelOptions);

        return (
            new DiagnosticsService.DiagnosticsServiceClient(channel),
            new PresetsService.PresetsServiceClient(channel),
            new SessionsService.SessionsServiceClient(channel),
            new ModulesService.ModulesServiceClient(channel),
            channel
        );
    }

    [Fact]
    public async Task Diagnostics_GetHealth_ShouldReturnHealthy()
    {
        var (diagnostics, _, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var response = await diagnostics.GetHealthAsync(new HealthCheckRequest());

            response.Should().NotBeNull();
            response.Status.Should().Be(HealthStatus.Healthy);
            response.Version.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Presets_Create_List_Get_Delete_ShouldRoundTrip()
    {
        var (_, presets, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var name = $"IntegrationTest-{Guid.NewGuid():N}";

            var created = await presets.CreatePresetAsync(new CreatePresetRequest
            {
                Preset = new Preset { Name = name }
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
        var (_, presets, _, _, channel) = await CreateAuthenticatedClientsAsync();

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
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

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
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

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
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

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
}