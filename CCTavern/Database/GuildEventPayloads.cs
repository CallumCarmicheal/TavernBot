namespace CCTavern.Database;

public sealed record TrackQueuedPayload(
    ulong Position,
    string Title,
    string TrackSource,
    ulong? PlaylistId);

public sealed record TrackRemovedPayload(
    ulong Position,
    string Title,
    string? Reason);

public sealed record TrackPlaybackPayload(
    ulong? Position,
    string Title,
    string? EndReason = null);

public sealed record TrackPositionChangedPayload(
    ulong FromPosition,
    ulong ToPosition);

public sealed record NextTrackChangedPayload(
    ulong OldPosition,
    ulong NewPosition);

public sealed record BooleanOptionChangedPayload(
    bool OldValue,
    bool NewValue);

public sealed record MusicChannelChangedPayload(
    ulong? OldChannelId,
    string? OldChannelName,
    ulong? NewChannelId,
    string? NewChannelName);

public sealed record PlayerConnectionPayload(
    ulong? VoiceChannelId,
    string? Reason = null);

public sealed record QueueClearedPayload(int RemovedTrackCount);
