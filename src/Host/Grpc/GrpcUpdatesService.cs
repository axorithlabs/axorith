using Axorith.Contracts;
using Axorith.Host.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Grpc;

public class GrpcUpdatesService : UpdatesService.UpdatesServiceBase
{
    private readonly UpdateService _updateService;

    public GrpcUpdatesService(UpdateService updateService)
    {
        _updateService = updateService;
    }

    public override async Task<UpdateInfoResponse> GetUpdateInfo(Empty request, ServerCallContext context)
    {
        await _updateService.WaitForInitialCheckAsync(context.CancellationToken);
        if (_updateService.LatestUpdate is null && _updateService.LastCheckError is { } error)
            throw new RpcException(new Status(StatusCode.Unavailable, error));

        return CreateResponse();
    }

    public override async Task<UpdateInfoResponse> CheckForUpdates(Empty request, ServerCallContext context)
    {
        await _updateService.CheckForUpdatesAsync(context.CancellationToken);
        if (_updateService.LastCheckError is { } error)
            throw new RpcException(new Status(StatusCode.Unavailable, error));

        return CreateResponse();
    }

    private UpdateInfoResponse CreateResponse()
    {
        var update = _updateService.LatestUpdate;
        var response = new UpdateInfoResponse
        {
            UpdateAvailable = update is not null,
            CurrentVersion = _updateService.CurrentVersion
        };

        if (update is not null)
        {
            response.LatestVersion = update.Version;
            response.DownloadUrl = update.DownloadUrl;
            response.ReleaseNotes = update.ReleaseNotes;
            response.PublishedAt = Timestamp.FromDateTime(update.PublishedAt.ToUniversalTime());
            response.Sha256Hash = update.Sha256Hash;
        }

        return response;
    }
}
