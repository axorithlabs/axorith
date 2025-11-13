using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Contracts;
using Axorith.Sdk;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly.Retry;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Client.CoreSdk.Grpc;

/// <summary>
///     gRPC implementation of IModulesApi with setting update streaming.
/// </summary>
internal class GrpcModulesApi(
    ModulesService.ModulesServiceClient client,
    AsyncRetryPolicy retryPolicy,
    ILogger logger)
    : IModulesApi, IDisposable
{
    private readonly Subject<SettingUpdate> _settingUpdatesSubject = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _instanceStreams = new();
    private bool _disposed;

    public IObservable<SettingUpdate> SettingUpdates => _settingUpdatesSubject.AsObservable();

    public IDisposable SubscribeToSettingUpdates(Guid moduleInstanceId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GrpcModulesApi));

        if (_instanceStreams.TryGetValue(moduleInstanceId, out var stream))
            return new CancellationDisposable(stream);

        var cts = new CancellationTokenSource();
        if (!_instanceStreams.TryAdd(moduleInstanceId, cts))
            return new CancellationDisposable(_instanceStreams[moduleInstanceId]);

        _ = StartStreamingSettingUpdatesAsync(moduleInstanceId, cts.Token);

        return Disposable.Create(() =>
        {
            if (!_instanceStreams.TryRemove(moduleInstanceId, out var existing)) return;

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
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.ListModulesAsync(
                    new ListModulesRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Modules
                .Select(ToModel)
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> BeginEditAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> initialValues, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var request = new BeginEditRequest
            {
                ModuleId = moduleId.ToString(),
                ModuleInstanceId = moduleInstanceId.ToString()
            };

            foreach (var (key, val) in initialValues)
            {
                var sv = new SettingValue { Key = key };
                switch (val)
                {
                    case string s:
                        sv.StringValue = s;
                        break;
                    case bool b:
                        sv.BoolValue = b;
                        break;
                    case int i:
                        sv.IntValue = i;
                        break;
                    case double d:
                        sv.NumberValue = d;
                        break;
                    case decimal dec:
                        sv.NumberValue = (double)dec;
                        break;
                    case null:
                        sv.StringValue = string.Empty;
                        break;
                    default:
                        sv.StringValue = val.ToString() ?? string.Empty;
                        break;
                }

                request.InitialValues.Add(sv);
            }

            var response = await client.BeginEditAsync(request, cancellationToken: ct).ConfigureAwait(false);
            return new OperationResult(response.Success, response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> EndEditAsync(Guid moduleInstanceId, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.EndEditAsync(new EndEditRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString()
            }, cancellationToken: ct).ConfigureAwait(false);

            return new OperationResult(response.Success, response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> SyncEditAsync(Guid moduleInstanceId, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.SyncEditAsync(new SyncEditRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString()
            }, cancellationToken: ct).ConfigureAwait(false);

            return new OperationResult(response.Success, response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    public async Task<ModuleSettingsInfo> GetModuleSettingsAsync(Guid moduleId, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.GetModuleSettingsAsync(
                    new GetModuleSettingsRequest { ModuleId = moduleId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            var settings = response.Settings
                .Select(s => new ModuleSetting(
                    s.Key,
                    s.Label,
                    string.IsNullOrEmpty(s.Description) ? null : s.Description,
                    s.ControlType.ToString(),
                    s.Persistence.ToString(),
                    s.IsVisible,
                    s.IsReadOnly,
                    s.ValueType,
                    s.ValueCase switch
                    {
                        Setting.ValueOneofCase.StringValue => s.StringValue,
                        Setting.ValueOneofCase.BoolValue => s.BoolValue.ToString(),
                        Setting.ValueOneofCase.NumberValue => s.NumberValue.ToString(),
                        Setting.ValueOneofCase.IntValue => s.IntValue.ToString(),
                        Setting.ValueOneofCase.DecimalString => s.DecimalString,
                        _ => string.Empty
                    },
                    s.Choices.Select(c => new KeyValuePair<string, string>(c.Key, c.Display)).ToList()
                ))
                .ToList();

            var actions = response.Actions
                .Select(a => new ModuleAction(
                    a.Key,
                    a.Label,
                    string.IsNullOrEmpty(a.Description) ? null : a.Description,
                    a.IsEnabled
                ))
                .ToList();

            return new ModuleSettingsInfo(settings, actions);
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> InvokeActionAsync(Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);

        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.InvokeActionAsync(
                    new InvokeActionRequest
                    {
                        ModuleInstanceId = moduleInstanceId.ToString(),
                        ActionKey = actionKey
                    },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return new OperationResult(
                response.Success,
                response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> UpdateSettingAsync(Guid moduleInstanceId, string settingKey,
        object? value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);

        return await retryPolicy.ExecuteAsync(async () =>
        {
            var request = new UpdateSettingRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString(),
                SettingKey = settingKey
            };

            switch (value)
            {
                case string s:
                    request.StringValue = s;
                    break;
                case bool b:
                    request.BoolValue = b;
                    break;
                case double d:
                    request.NumberValue = d;
                    break;
                case int i:
                    request.IntValue = i;
                    break;
                case decimal dec:
                    request.NumberValue = (double)dec;
                    break;
                case null:
                    request.StringValue = string.Empty;
                    break;
                default:
                    request.StringValue = value.ToString() ?? string.Empty;
                    break;
            }

            var response = await client.UpdateSettingAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return new OperationResult(
                response.Success,
                response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    private async Task StartStreamingSettingUpdatesAsync(Guid moduleInstanceId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                logger.LogDebug("Starting setting updates stream...");

                using var call = client.StreamSettingUpdates(
                    new StreamSettingUpdatesRequest { ModuleInstanceId = moduleInstanceId.ToString() },
                    cancellationToken: ct);

                await foreach (var update in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    if (!Guid.TryParse(update.ModuleInstanceId, out var instanceId))
                        continue;

                    object? value = update.ValueCase switch
                    {
                        Contracts.SettingUpdate.ValueOneofCase.StringValue => update.StringValue,
                        Contracts.SettingUpdate.ValueOneofCase.BoolValue => update.BoolValue,
                        Contracts.SettingUpdate.ValueOneofCase.NumberValue => update.NumberValue,
                        Contracts.SettingUpdate.ValueOneofCase.IntValue => update.IntValue,
                        Contracts.SettingUpdate.ValueOneofCase.ChoiceList => update.ChoiceList.Choices
                            .Select(c => new KeyValuePair<string, string>(c.Key, c.Display))
                            .ToList(),
                        _ => null
                    };

                    var settingUpdate = new SettingUpdate(
                        instanceId,
                        update.SettingKey,
                        (SettingProperty)update.Property,
                        value);

                    _settingUpdatesSubject.OnNext(settingUpdate);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                logger.LogDebug("Setting updates stream cancelled");
                break;
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Setting updates stream cancelled");
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Setting updates stream error, reconnecting in 5s...");

                try
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
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
            return;

        _disposed = true;

        _settingUpdatesSubject.OnCompleted();
        _settingUpdatesSubject.Dispose();

        foreach (var kv in _instanceStreams)
            try
            {
                kv.Value.Cancel();
                kv.Value.Dispose();
            }
            catch
            {
            }

        _instanceStreams.Clear();

        GC.SuppressFinalize(this);
    }
}