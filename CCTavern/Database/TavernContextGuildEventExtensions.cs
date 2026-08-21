using DSharpPlus.Entities;

using Microsoft.EntityFrameworkCore;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Database;

public static class TavernContextGuildEventExtensions {
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() }
    };

    public static GuildEventRecord AddGuildEvent<TPayload>(
        this TavernContext database,
        ulong guildId,
        GuildEventKind kind,
        GuildEventSource source,
        TPayload payload,
        ResolvedGuildUser? actor = null,
        ulong? messageId = null,
        GuildQueueItem? queueItem = null,
        ulong? queueItemId = null,
        Guid? correlationId = null,
        ushort payloadVersion = 1
    ) {
        ArgumentNullException.ThrowIfNull(database);

        if (queueItem != null && queueItemId.HasValue)
            throw new ArgumentException("Specify either a queue item or a queue item ID, not both.");

        var guildEvent = new GuildEventRecord {
            GuildId = guildId,
            ActorUserId = actor?.User.Id,
            ActorUser = actor?.User,
            ActorUsername = actor?.User.Username,
            ActorDisplayName = actor?.GuildUser.DisplayName,
            Kind = kind,
            Source = source,
            MessageId = messageId,
            QueueItemId = queueItemId,
            QueueItem = queueItem,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            PayloadVersion = payloadVersion,
            PayloadJson = JsonSerializer.Serialize(payload, PayloadSerializerOptions)
        };

        database.GuildEvents.Add(guildEvent);
        return guildEvent;
    }

    public static async Task<GuildEventRecord> AddGuildEventAsync<TPayload>(
        this TavernContext database,
        ulong guildId,
        GuildEventKind kind,
        GuildEventSource source,
        TPayload payload,
        DiscordMember? actor = null,
        ulong? messageId = null,
        GuildQueueItem? queueItem = null,
        ulong? queueItemId = null,
        Guid? correlationId = null,
        ushort payloadVersion = 1,
        CancellationToken cancellationToken = default
    ) {
        ResolvedGuildUser? resolvedActor = actor == null
            ? null
            : await database.ResolveGuildUserAsync(actor, cancellationToken);

        return database.AddGuildEvent(
            guildId,
            kind,
            source,
            payload,
            resolvedActor,
            messageId,
            queueItem,
            queueItemId,
            correlationId,
            payloadVersion);
    }
}

public sealed class GuildEventWriter {
    private readonly IDbContextFactory<TavernContext> databaseFactory;

    public GuildEventWriter(IDbContextFactory<TavernContext> databaseFactory) {
        this.databaseFactory = databaseFactory;
    }

    public async Task WriteAsync<TPayload>(
        ulong guildId,
        GuildEventKind kind,
        GuildEventSource source,
        TPayload payload,
        DiscordMember? actor = null,
        ulong? messageId = null,
        ulong? queueItemId = null,
        Guid? correlationId = null,
        ushort payloadVersion = 1,
        CancellationToken cancellationToken = default
    ) {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        await database.AddGuildEventAsync(
            guildId,
            kind,
            source,
            payload,
            actor,
            messageId,
            queueItemId: queueItemId,
            correlationId: correlationId,
            payloadVersion: payloadVersion,
            cancellationToken: cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }
}