using System;

namespace CCTavern.Database;

public enum GuildEventKind {
    TrackQueued,
    TrackRemoved,
    TrackStarted,
    TrackFinished,
    TrackPaused,
    TrackResumed,
    TrackSeeked,
    TrackSkipped,
    TrackJumped,
    NextTrackChanged,
    QueueCleared,
    QueueShuffled,
    ShuffleChanged,
    RepeatChanged,
    LeaveAfterQueueChanged,
    MusicChannelChanged,
    PrefixesChanged,
    YoutubeTrackListSet,
    PlayerConnected,
    PlayerDisconnected
}

public enum GuildEventSource {
    DiscordCommand,
    Player,
    System,
    Import,
    WebApi
}

public sealed class GuildEventRecord {
    public ulong Id { get; set; }

    public ulong GuildId { get; set; }
    public Guild Guild { get; set; } = null!;

    public ulong? ActorUserId { get; set; }
    public TavernUser? ActorUser { get; set; }

    public string? ActorUsername { get; set; }
    public string? ActorDisplayName { get; set; }

    public GuildEventKind Kind { get; set; }
    public GuildEventSource Source { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public ulong? MessageId { get; set; }

    public ulong? QueueItemId { get; set; }
    public GuildQueueItem? QueueItem { get; set; }

    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public ushort PayloadVersion { get; set; } = 1;
    public string PayloadJson { get; set; } = "{}";
}