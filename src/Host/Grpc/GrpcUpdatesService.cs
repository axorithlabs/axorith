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
            PopulateResponse(response, _updateService.LatestUpdate);
        }

        return response;
    }

    /// <summary>
    ///     Returns update info without version check — callable even when client/host versions are incompatible.
    ///     Used by ErrorViewModel's "Update and Restart" button.
    /// </summary>
    public override async Task<UpdateInfoResponse> GetLatestUpdateInfo(Empty request, ServerCallContext context)
    {
        var updateInfo = await _updateService.GetUpdateInfoAsync(context.CancellationToken);

        var response = new UpdateInfoResponse
        {
            UpdateAvailable = updateInfo != null,
            CurrentVersion = _updateService.CurrentVersion
        };

        if (updateInfo != null)
        {
            PopulateResponse(response, updateInfo);
        }

        return response;
    }

    private static void PopulateResponse(UpdateInfoResponse response, Models.UpdateInfo updateInfo)
    {
        response.LatestVersion = updateInfo.Version;
        response.DownloadUrl = updateInfo.DownloadUrl;
        response.ReleaseNotes = updateInfo.ReleaseNotes;
        response.PublishedAt = Timestamp.FromDateTime(updateInfo.PublishedAt.ToUniversalTime());
        response.Sha256Hash = updateInfo.Sha256;
        response.Platform = updateInfo.Platform;
        response.Architecture = updateInfo.Architecture;
        response.InstallerType = updateInfo.InstallerType;
    }
}