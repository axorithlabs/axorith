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
internal class GrpcModulesApi : IModulesApi, IDisposable
{
    private readonly ModulesService.ModulesServiceClient _client;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger _logger;
    private readonly Subject<SettingUpdate> _settingUpdatesSubject;
    private readonly CancellationTokenSource _streamCts;
    private readonly Task? _streamTask;
    private bool _disposed;

    public GrpcModulesApi(ModulesService.ModulesServiceClient client, AsyncRetryPolicy retryPolicy,
        ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _settingUpdatesSubject = new Subject<SettingUpdate>();
        _streamCts = new CancellationTokenSource();

        // Start streaming setting updates immediately
        _streamTask = StartStreamingSettingUpdatesAsync(_streamCts.Token);
    }

    public IObservable<SettingUpdate> SettingUpdates => _settingUpdatesSubject.AsObservable();

    public async Task<IReadOnlyList<ModuleDefinition>> ListModulesAsync(CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.ListModulesAsync(
                    new ListModulesRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Modules
                .Select(ToModel)
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<ModuleSettingsInfo> GetModuleSettingsAsync(Guid moduleId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.GetModuleSettingsAsync(
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

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.InvokeActionAsync(
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

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var request = new UpdateSettingRequest
            {
                ModuleInstanceId = moduleInstanceId.ToString(),
                SettingKey = settingKey
            };

            // Set value based on type
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

            var response = await _client.UpdateSettingAsync(request, cancellationToken: ct)
                .ConfigureAwait(false);

            return new OperationResult(
                response.Success,
                response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    private async Task StartStreamingSettingUpdatesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                _logger.LogInformation("Starting setting updates stream...");

                using var call = _client.StreamSettingUpdates(
                    new StreamSettingUpdatesRequest(),
                    cancellationToken: ct);

                await foreach (var update in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    // Convert protobuf update to CoreSdk update
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
                _logger.LogInformation("Setting updates stream cancelled");
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Setting updates stream cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Setting updates stream error, reconnecting in 5s...");

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

        _streamCts.Cancel();
        _streamCts.Dispose();

        _settingUpdatesSubject.OnCompleted();
        _settingUpdatesSubject.Dispose();

        if (_streamTask != null)
            try
            {
                _streamTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore timeout
            }

        GC.SuppressFinalize(this);
    }
}