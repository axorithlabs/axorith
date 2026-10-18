using Axorith.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Axorith.Host.Mappers;

public static class SessionMapper
{
    public static SessionEvent CreateEvent(SessionEventType type, Guid? presetId, string? message = null)
    {
        return new SessionEvent
        {
            Type = type,
            PresetId = presetId?.ToString() ?? string.Empty,
            Message = message ?? string.Empty,
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };
    }

    public static OperationResult CreateResult(bool success, string? message = null,
        IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null)
    {
        var result = new OperationResult
        {
            Success = success,
            Message = message ?? (success ? "Operation completed successfully" : "Operation failed")
        };

        result.Errors.AddRange(errors ?? []);
        result.Warnings.AddRange(warnings ?? []);

        return result;
    }
}
