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
        // Wait for initial check to complete (with timeout)
        await _updateService.WaitForInitialCheckAsync(context.CancellationToken);

        var response = new UpdateInfoResponse
        {
            UpdateAvailable = _updateService.UpdateAvailable,
            CurrentVersion = _updateService.CurrentVersion
        };

        if (_updateService.LatestUpdate != null)
        {
            response.LatestVersion = _updateService.LatestUpdate.Version;
            response.DownloadUrl = _updateService.LatestUpdate.DownloadUrl;
            response.ReleaseNotes = _updateService.LatestUpdate.ReleaseNotes;
            response.PublishedAt = Timestamp.FromDateTime(_updateService.LatestUpdate.PublishedAt.ToUniversalTime());
        }

        return response;
    }
}