using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Sdk;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly.Retry;
using Action = Axorith.Contracts.Action;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;
using OperationResult = Axorith.Client.CoreSdk.Abstractions.OperationResult;
using SettingProperty = Axorith.Client.CoreSdk.Abstractions.SettingProperty;
using SettingUpdate = Axorith.Client.CoreSdk.Abstractions.SettingUpdate;
using ValidationResult = Axorith.Sdk.ValidationResult;

namespace Axorith.Client.CoreSdk;

internal class GrpcModulesApi(
    ModulesService.ModulesServiceClient client,
    AsyncRetryPolicy retryPolicy,
    ILogger logger)
    : IModulesApi, IDisposable
{
    private readonly Subject<SettingUpdate> _settingUpdatesSubject = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _instanceStreams = new();
    private readonly ConcurrentDictionary<Guid, ModuleSettingsInfo> _settingsCache = new();
    private readonly SemaphoreSlim _modulesCacheLock = new(1, 1);
    private IReadOnlyList<ModuleDefinition>? _modulesCache;
    private bool _disposed;

    public IObservable<SettingUpdate> SettingUpdates => _settingUpdatesSubject.AsObservable();

    public async Task<IDisposable> SubscribeToSettingUpdatesAsync(Guid moduleInstanceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(GrpcModulesApi));

        if (_instanceStreams.TryGetValue(moduleInstanceId, out var stream))
        {
            return Disposable.Create(() => { });
        }

        var cts = new CancellationTokenSource();
        if (!_instanceStreams.TryAdd(moduleInstanceId, cts))
        {
            return Disposable.Create(() => { });
        }

        var connectionReadyTcs = new TaskCompletionSource();

        _ = StartStreamingSettingUpdatesAsync(moduleInstanceId, cts.Token, connectionReadyTcs);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        var completedTask = await Task.WhenAny(connectionReadyTcs.Task, timeoutTask).ConfigureAwait(false);

        if (completedTask == timeoutTask)
        {
            logger.LogWarning("Setting updates stream connection timed out for {InstanceId}", moduleInstanceId);
        }

        return Disposable.Create(() =>
        {
            if (!_instanceStreams.TryRemove(moduleInstanceId, out var existing))
            {
                return;
            }

            try
            {
                existing.Cancel();
                existing.Dispose();
            }
            catch
            {
                // ignored
            }
        });
    }

    public async Task<IReadOnlyList<ModuleDefinition>> ListModulesAsync(CancellationToken ct = default)
    {
        if (_modulesCache != null)
        {
            return _modulesCache;
        }

        await _modulesCacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_modulesCache != null)
            {
                return _modulesCache;
            }

            var modules = await retryPolicy.ExecuteAsync(async () =>
            {
                var response = await client.ListModulesAsync(
                        new ListModulesRequest(),
                        cancellationToken: ct)
                    .ConfigureAwait(false);

                return response.Modules
                    .Select(ToModel)
                    .ToList();
            }).ConfigureAwait(false);

            _modulesCache = modules;
            return modules;
        }
        finally
        {
            _modulesCacheLock.Release();
        }
    }

    public Task<BeginEditResult> BeginEditAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> initialValues, CancellationToken ct = default)
    {
        return retryPolicy.ExecuteAsync(async () =>
        {
            var request = new BeginEditRequest
            {
                ModuleId = moduleId.ToString(),
                ModuleInstanceId = moduleInstanceId.ToString()
            };

            foreach (var (key, val) in initialValues)
            {
                request.InitialValues.Add(SettingValueCodec.Create(key, val));
            }

            var response = await client.BeginEditAsync(request, cancellationToken: ct).ConfigureAwait(false);
            var settingsInfo = MapSettingsResponse(response.Settings, response.Actions);

            _settingsCache[moduleId] = settingsInfo;

            var opResult = GrpcResultMapper.ToModel(response.Result);

            return new BeginEditResult(settingsInfo, opResult);
        });
    }

    public Task<OperationResult> EndEditAsync(Guid moduleInstanceId, CancellationToken ct = default)
    {
        return retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.EndEditAsync(new EndEditRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString()
            }, cancellationToken: ct).ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public Task<OperationResult> SyncEditAsync(Guid moduleInstanceId, CancellationToken ct = default)
    {
        return retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.SyncEditAsync(new SyncEditRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString()
            }, cancellationToken: ct).ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public Task<ModuleSettingsInfo> GetModuleSettingsAsync(Guid moduleId, CancellationToken ct = default)
    {
        return retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.GetModuleSettingsAsync(
                    new GetModuleSettingsRequest { ModuleId = moduleId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);
            var info = MapSettingsResponse(response.Settings, response.Actions);
            _settingsCache[moduleId] = info;
            return info;
        });
    }

    public Task<OperationResult> InvokeActionAsync(Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);

        return retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.InvokeActionAsync(
                    new InvokeActionRequest
                    {
                        ModuleInstanceId = moduleInstanceId.ToString(),
                        ActionKey = actionKey
                    },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public Task<OperationResult> InvokeDesignTimeActionAsync(Guid moduleId, Guid moduleInstanceId,
        string actionKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);

        return retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.InvokeDesignTimeActionAsync(
                    new InvokeDesignTimeActionRequest
                    {
                        ModuleId = moduleId.ToString(),
                        ModuleInstanceId = moduleInstanceId.ToString(),
                        ActionKey = actionKey
                    },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public Task<OperationResult> UpdateSettingAsync(Guid moduleInstanceId, string settingKey,
        object? value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);

        return retryPolicy.ExecuteAsync(async () =>
        {
            var request = new UpdateSettingRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString(),
                SettingKey = settingKey
            };

            SettingValueCodec.Set(request, value);

            var response = await client.UpdateSettingAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public Task<ValidationResult> ValidateSettingsAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        return retryPolicy.ExecuteAsync(async () =>
        {
            var request = new ValidateSettingsRequest
            {
                ModuleId = moduleId.ToString(),
                ModuleInstanceId = moduleInstanceId.ToString()
            };

            foreach (var (key, val) in values)
            {
                request.Values.Add(SettingValueCodec.Create(key, val));
            }

            var response = await client.ValidateSettingsAsync(request, cancellationToken: ct).ConfigureAwait(false);

            if (response.IsValid)
            {
                return response.IsWarning ? ValidationResult.Warn(response.Message) : ValidationResult.Success;
            }

            var fieldErrors = new Dictionary<string, string>();
            foreach (var error in response.FieldErrors)
            {
                fieldErrors[error.SettingKey] = error.ErrorMessage;
            }

            return ValidationResult.Fail(fieldErrors, response.Message);
        });
    }

    public ModuleSettingsInfo? GetCachedSettings(Guid moduleId) => _settingsCache.TryGetValue(moduleId, out var cached) ? cached : null;


    private static ModuleSettingsInfo MapSettingsResponse(
        IEnumerable<Setting> settings,
        IEnumerable<Action> actions)
    {
        var mappedSettings = settings
            .Select(s => new ModuleSetting(
                s.Key,
                s.Label,
                string.IsNullOrEmpty(s.Description) ? null : s.Description,
                s.ControlType.ToString(),
                s.Persistence.ToString(),
                s.IsVisible,
                s.IsReadOnly,
                s.ValueType,
                SettingValueCodec.GetString(s),
                s.Choices.Select(c => new KeyValuePair<string, string>(c.Key, c.Display)).ToList(),
                string.IsNullOrWhiteSpace(s.Filter) ? null : s.Filter,
                s.HasHistory
            ))
            .ToList();

        var mappedActions = actions
            .Select(a => new ModuleAction(
                a.Key,
                a.Label,
                string.IsNullOrEmpty(a.Description) ? null : a.Description,
                a.IsEnabled,
                string.IsNullOrEmpty(a.SettingKey) ? null : a.SettingKey
            ))
            .ToList();

        return new ModuleSettingsInfo(mappedSettings, mappedActions);
    }

    private Task StartStreamingSettingUpdatesAsync(Guid moduleInstanceId, CancellationToken ct,
        TaskCompletionSource? readyTcs = null)
    {
        var hasSignaledReady = false;
        return GrpcStreamRunner.RunAsync(async token =>
        {
            logger.LogDebug("Starting setting updates stream...");
            using var call = client.StreamSettingUpdates(
                new StreamSettingUpdatesRequest { ModuleInstanceId = moduleInstanceId.ToString() },
                cancellationToken: token);

            await call.ResponseHeadersAsync.ConfigureAwait(false);
            if (!hasSignaledReady)
            {
                readyTcs?.TrySetResult();
                hasSignaledReady = true;
            }

            await foreach (var update in call.ResponseStream.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (!Guid.TryParse(update.ModuleInstanceId, out var instanceId))
                {
                    continue;
                }

                _settingUpdatesSubject.OnNext(new SettingUpdate(
                    instanceId,
                    update.SettingKey,
                    (SettingProperty)update.Property,
                    SettingValueCodec.Get(update)));
            }
        }, logger, "Setting updates", ct);
    }

    private static ModuleDefinition ToModel(Contracts.ModuleDefinition message)
    {
        var platforms = message.Platforms
            .Select(p => Enum.TryParse<Platform>(p, out var platform) ? platform : Platform.Windows)
            .ToArray();

        return new ModuleDefinition
        {
            Id = Guid.Parse(message.Id),
            Name = message.Name,
            Description = message.Description,
            Category = message.Category,
            Platforms = platforms,
            AssemblyFileName = message.Assembly
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _settingUpdatesSubject.OnCompleted();
        _settingUpdatesSubject.Dispose();

        foreach (var kv in _instanceStreams)
        {
            kv.Value.Cancel();
            kv.Value.Dispose();
        }

        _instanceStreams.Clear();

        GC.SuppressFinalize(this);
    }
}
