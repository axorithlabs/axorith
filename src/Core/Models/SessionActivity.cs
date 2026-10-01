namespace Axorith.Core.Models;

public sealed record SessionActivity(DateTimeOffset StartedAt, DateTimeOffset EndedAt, string PresetName);
