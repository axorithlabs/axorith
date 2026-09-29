using System.Text.Json;
using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Tests.Helpers;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

public sealed class SessionPreflightTelemetryTests
{
    [Fact]
    public async Task ManualStart_EmitsOneCanonicalPreflightEventFromHostForTheAttempt()
    {
        await using var server = new PostHogTestServer();

        var directory = Directory.CreateTempSubdirectory("axorith-session-telemetry-");
        try
        {
            var distinctId = Guid.NewGuid().ToString("D");
            await using var telemetry = new TelemetryService(new TelemetrySettings
            {
                Enabled = true,
                PostHogApiKey = "test-project-key",
                PostHogHost = server.Host,
                DistinctId = distinctId,
                ApplicationName = "Axorith.Host",
                AppVersion = "1.2.3",
                OsVersion = "Windows 11",
                BatchSize = 100,
                FlushInterval = TimeSpan.FromHours(1)
            });

            var presetId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var sessionInstanceId = Guid.NewGuid();
            var preset = new SessionPreset(presetId)
            {
                Name = "Telemetry test",
                Modules = [new ConfiguredModule { ModuleId = moduleId }],
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(1)
                }
            };

            await using var sessionManager = new SessionManager(
                new TestModuleRegistry(moduleId),
                NullLogger<SessionManager>.Instance,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                telemetry,
                Path.Combine(directory.FullName, "committed-session.json"));

            telemetry.TrackEvent("SessionStartRequested", new Dictionary<string, object?>
            {
                ["sessionInstanceId"] = sessionInstanceId,
                ["presetId"] = presetId,
                ["startSource"] = "manual"
            });
            await sessionManager.StartSessionAsync(preset, startSource: "manual", sessionInstanceId: sessionInstanceId);
            await Assert.ThrowsAsync<SessionException>(() =>
                sessionManager.StartSessionAsync(preset, startSource: "manual", sessionInstanceId: sessionInstanceId));
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));

            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await telemetry.FlushAsync(flushCts.Token);

            var events = server.Payloads
                .SelectMany(body =>
                {
                    using var payload = JsonDocument.Parse(body);
                    return payload.RootElement.GetProperty("batch").EnumerateArray()
                        .Select(item => item.Clone()).ToArray();
                })
                .ToArray();
            var names = events.Select(item => item.GetProperty("event").GetString()).ToArray();
            var requestIndex = Array.IndexOf(names, "SessionStartRequested");
            var preflightIndex = Array.IndexOf(names, "SessionPreflightCompleted");
            var startedIndex = Array.IndexOf(names, "SessionStarted");

            Assert.Equal(1, names.Count(name => name == "SessionStartRequested"));
            Assert.Equal(1, names.Count(name => name == "SessionPreflightCompleted"));
            Assert.Equal(1, names.Count(name => name == "SessionStarted"));
            Assert.True(requestIndex < preflightIndex && preflightIndex < startedIndex);
            var attemptEvents = events.Where(item => item.GetProperty("event").GetString() is
                "SessionStartRequested" or "SessionPreflightCompleted" or "SessionStarted");
            Assert.All(attemptEvents, item =>
            {
                Assert.Equal(distinctId, item.GetProperty("distinct_id").GetString());
                Assert.Equal(sessionInstanceId.ToString("D"),
                    item.GetProperty("properties").GetProperty("sessionInstanceId").GetString());
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class TestModuleRegistry(Guid moduleId) : IModuleRegistry
    {
        private readonly ModuleDefinition _definition = new() { Id = moduleId, Name = "Test" };

        public IReadOnlyList<ModuleDefinition> GetAllDefinitions() => [_definition];
        public ModuleDefinition? GetDefinitionById(Guid id) => id == moduleId ? _definition : null;

        public (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid id)
        {
            if (id != moduleId) return (null, null);

            var builder = new ContainerBuilder();
            builder.RegisterInstance(_definition).As<ModuleDefinition>();
            return (new TestModule(), builder.Build());
        }
    }

    private sealed class TestModule : IModule
    {
        public IReadOnlyList<Axorith.Sdk.Settings.ISetting> GetSettings() => [];
        public IReadOnlyList<Axorith.Sdk.Actions.IAction> GetActions() => [];
        public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ValidationResult.Success);
        public Task OnSessionStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnSessionEndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
