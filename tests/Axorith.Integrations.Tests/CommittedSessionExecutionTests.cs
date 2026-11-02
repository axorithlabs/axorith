using System.Diagnostics;
using Axorith.Client.CoreSdk;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Xunit;
using FocusCommitmentMode = Axorith.Core.Models.FocusCommitmentMode;
using FocusCommitmentOptions = Axorith.Core.Models.FocusCommitmentOptions;
using FocusEndCondition = Axorith.Core.Models.FocusEndCondition;

namespace Axorith.Integrations.Tests;

public sealed partial class SessionDashboardRegressionTests
{
    [WindowsFact]
    public async Task HostRestartRecoversTheSameDeadlineAndUsedBreakWithoutLeavingAnOldBlockerRunning()
    {
        var data = Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("AXORITH_ROAMING_ROOT")!, "recovery-" + Guid.NewGuid().ToString("N")));
        var processName = "axorith-recovery-" + Guid.NewGuid().ToString("N");
        var executable = Path.Combine(data.FullName, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);
        var moduleId = InstallModule(data.FullName, typeof(Axorith.Module.AppBlocker.Module), "App Blocker");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Guid presetId;
        DateTimeOffset? endsAt;
        DateTimeOffset? breakEndsAt;
        await using (var host = new RealHost(data.FullName, NoopTelemetryService.Instance))
        {
            using var channel = host.Connect(data.FullName);
            using var api = new GrpcSessionsApi(new SessionsService.SessionsServiceClient(channel),
                Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
            var presets = new GrpcPresetsApi(new PresetsService.PresetsServiceClient(channel), Policy.Handle<Exception>().RetryAsync(0));
            var preset = await presets.CreatePresetAsync(new SessionPreset
            {
                Id = Guid.NewGuid(), Name = "Recover real protection",
                Modules = [new Axorith.Core.Models.ConfiguredModule
                {
                    ModuleId = moduleId, Settings = new Dictionary<string, string>
                    { ["Mode"] = "BlockList", ["Categories"] = "", ["CustomProcessList"] = processName }
                }],
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked, EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(5), BreakCount = 1, BreakDuration = TimeSpan.FromMinutes(1)
                }
            }, deadline.Token);
            presetId = preset.Id;
            Assert.True((await api.StartSessionAsync(preset.Id, Guid.NewGuid(), deadline.Token)).Success);
            await AssertProbeBlocked();
            Assert.True((await api.StartBreakAsync(deadline.Token)).Success);
            var state = (await api.GetCurrentSessionAsync(deadline.Token))!;
            endsAt = state.EndsAt;
            breakEndsAt = state.BreakEndsAt;
            Assert.NotNull(endsAt);
            Assert.NotNull(breakEndsAt);
            Assert.Equal(0, state.BreaksRemaining);
        }

        await using var recoveredHost = new RealHost(data.FullName, NoopTelemetryService.Instance);
        using var recoveredChannel = recoveredHost.Connect(data.FullName);
        using var recoveredApi = new GrpcSessionsApi(new SessionsService.SessionsServiceClient(recoveredChannel),
            Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
        var manager = recoveredHost.Services.GetRequiredService<ISessionManager>();
        try
        {
            var recovered = (await recoveredApi.GetCurrentSessionAsync(deadline.Token))!;
            Assert.NotNull(recovered);
            Assert.Equal(presetId, recovered.PresetId);
            Assert.Equal(endsAt, recovered.EndsAt);
            Assert.Equal(breakEndsAt, recovered.BreakEndsAt);
            Assert.Equal(0, recovered.BreaksRemaining);
            Assert.Empty(await recoveredApi.GetSessionHistoryAsync(deadline.Token));
            Assert.False((await recoveredApi.StartBreakAsync(deadline.Token)).Success);
            await AssertProbeSurvives();
            // The same maintenance operation used when the persisted break expires.
            await manager.EndBreakAsync(deadline.Token);
            await AssertProbeBlocked();
            await manager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock, deadline.Token);
            await AssertProbeSurvives();
            Assert.Single(await recoveredApi.GetSessionHistoryAsync(deadline.Token));
        }
        finally
        {
            if (manager.IsSessionRunning) await manager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
        }

        Process StartProbe() => Process.Start(new ProcessStartInfo(executable, "-t 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true })!;
        async Task AssertProbeBlocked()
        {
            using var process = StartProbe();
            try { await process.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
        }
        async Task AssertProbeSurvives()
        {
            using var process = StartProbe();
            try { await Task.Delay(1200, deadline.Token); Assert.False(process.HasExited); }
            finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
        }
    }

    [WindowsFact]
    public async Task LockedBreakResumesRealBlockingAndEmergencyUnlockRequiresAnUninterruptedHold()
    {
        var data = Directory.CreateDirectory(Path.Combine(Environment.GetEnvironmentVariable("AXORITH_ROAMING_ROOT")!,
            "committed-" + Guid.NewGuid().ToString("N")));
        var processName = "axorith-break-" + Guid.NewGuid().ToString("N");
        var executable = Path.Combine(data.FullName, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);
        var moduleId = InstallModule(data.FullName, typeof(Axorith.Module.AppBlocker.Module), "App Blocker");
        // Exercise real session/blocker code while keeping Windows sign-in policy out of this test.
        await using var host = new RealHost(data.FullName, NoopTelemetryService.Instance);
        using var channel = host.Connect(data.FullName);
        using var api = new GrpcSessionsApi(new SessionsService.SessionsServiceClient(channel),
            Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
        var presets = new GrpcPresetsApi(new PresetsService.PresetsServiceClient(channel), Policy.Handle<Exception>().RetryAsync(0));
        await ((Axorith.Core.Services.ModuleRegistry)host.Services.GetRequiredService<IModuleRegistry>()).InitializeAsync(CancellationToken.None);
        Process? probe = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(95));
        var manager = host.Services.GetRequiredService<ISessionManager>();
        try
        {
            var preset = await presets.CreatePresetAsync(new SessionPreset
            {
                Id = Guid.NewGuid(), Name = "Real committed blocking",
                Modules = [new Axorith.Core.Models.ConfiguredModule
                {
                    ModuleId = moduleId,
                    Settings = new Dictionary<string, string>
                    {
                        ["Mode"] = "BlockList", ["Categories"] = "", ["CustomProcessList"] = processName
                    }
                }],
                FocusCommitment = new Axorith.Core.Models.FocusCommitmentOptions
                {
                    Mode = Axorith.Core.Models.FocusCommitmentMode.Locked,
                    EndCondition = Axorith.Core.Models.FocusEndCondition.Duration, Duration = TimeSpan.FromMinutes(5),
                    BreakCount = 1, BreakDuration = TimeSpan.FromMinutes(1)
                }
            });
            var started = await api.StartSessionAsync(preset.Id, Guid.NewGuid(), deadline.Token);
            Assert.True(started.Success, started.Message);
            Assert.False((await api.StopSessionAsync(deadline.Token)).Success);
            probe = StartProbe();
            await probe.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(5));
            probe.Dispose();

            var breakResult = await api.StartBreakAsync(deadline.Token);
            Assert.True(breakResult.Success, breakResult.Message);
            var state = (await api.GetCurrentSessionAsync(deadline.Token))!;
            Assert.NotNull(state.BreakEndsAt);
            Assert.Equal(0, state.BreaksRemaining);
            probe = StartProbe();
            await Task.Delay(1500, deadline.Token);
            Assert.False(probe.HasExited, "The real blocker must pause during a break.");

            using var unlock = new SessionsService.SessionsServiceClient(channel).HoldEmergencyUnlock(cancellationToken: deadline.Token);
            async Task<double> HoldOnce()
            {
                await unlock.RequestStream.WriteAsync(new EmergencyUnlockHoldSignal { IsHeld = true }, deadline.Token);
                Assert.True(await unlock.ResponseStream.MoveNext(deadline.Token));
                Assert.False(unlock.ResponseStream.Current.Completed);
                return unlock.ResponseStream.Current.Progress;
            }
            Assert.Equal(0, await HoldOnce());
            await Task.Delay(200, deadline.Token);
            Assert.True(await HoldOnce() > 0);
            await unlock.RequestStream.WriteAsync(new EmergencyUnlockHoldSignal { IsHeld = false }, deadline.Token);
            Assert.Equal(0, await HoldOnce());
            await Task.Delay(1200, deadline.Token);
            Assert.Equal(0, await HoldOnce());

            var release = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(100, deadline.Token);
                    await unlock.RequestStream.WriteAsync(new EmergencyUnlockHoldSignal { IsHeld = true }, deadline.Token);
                    Assert.True(await unlock.ResponseStream.MoveNext(deadline.Token));
                    if (unlock.ResponseStream.Current.Completed) return;
                }
            }, deadline.Token);
            await probe.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(65));
            do
            {
                state = (await api.GetCurrentSessionAsync(deadline.Token))!;
                Assert.NotNull(state);
                if (state.BreakEndsAt is not null) await Task.Delay(25, deadline.Token);
            } while (state.BreakEndsAt is not null);
            Assert.True(state.IsActive, "Break expiry must resume protection before Emergency Unlock completes.");
            Assert.Null(state.BreakEndsAt);
            Assert.False((await api.StartBreakAsync(deadline.Token)).Success);
            await release;
            Assert.Null(await api.GetCurrentSessionAsync(deadline.Token));
            Assert.Contains(await api.GetSessionHistoryAsync(deadline.Token), activity => activity.PresetName == preset.Name);
            probe.Dispose();
            probe = StartProbe();
            await Task.Delay(1200, deadline.Token);
            Assert.False(probe.HasExited, "Emergency Unlock must actually remove blocking.");
        }
        finally
        {
            deadline.Cancel();
            if (manager.IsSessionRunning) await manager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            if (probe is not null)
            {
                if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync(); }
                probe.Dispose();
            }
        }

        Process StartProbe() => Process.Start(new ProcessStartInfo(executable, "-t 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true })!;
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Real App Blocker execution requires Windows.";
        }
    }
}
