using CCTavern.Database;
using CCTavern.Player;

using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

using Lavalink4NET;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Filters;
using Lavalink4NET.Integrations.ExtraFilters;
using Lavalink4NET.Player;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MySqlX.XDevAPI.Common;

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ZstdSharp.Unsafe;

namespace CCTavern.Commands {
    public class BaseAudioCommandModule : BaseCommandModule {
        protected readonly IAudioService audioService;
        protected readonly MusicBotHelper mbHelper;
        protected readonly IDbContextFactory<TavernContext> databaseFactory;

        public BaseAudioCommandModule(IAudioService audioService, MusicBotHelper mbHelper,
                IDbContextFactory<TavernContext> databaseFactory) {
            this.audioService = audioService;
            this.mbHelper = mbHelper;
            this.databaseFactory = databaseFactory;
        }

        protected async ValueTask<(PlayerResult<TavernPlayer> playerResult, bool isPlayerConnected)> GetPlayerAsync(ulong guildId, ulong? voiceChannelId = null, bool connectToVoiceChannel = true) {
            return await mbHelper.GetPlayerAsync(guildId, voiceChannelId, connectToVoiceChannel);
        }

        protected string GetPlayerErrorMessage(PlayerRetrieveStatus status) => mbHelper.GetPlayerErrorMessage(status);

        protected async Task RecordCurrentTrackCommandEventAsync<TPayload>(
            CommandContext context,
            GuildEventKind kind,
            TPayload payload
        ) {
            await using var database = await databaseFactory.CreateDbContextAsync();
            var guild = await database.GetOrCreateDiscordGuild(context.Guild);
            var queueItem = await database.GuildQueueItems.FirstOrDefaultAsync(
                x => x.GuildId == guild.Id && x.Position == guild.CurrentTrack);

            await database.AddGuildEventAsync(
                guild.Id,
                kind,
                GuildEventSource.DiscordCommand,
                payload,
                context.Member,
                messageId: context.Message.Id,
                queueItem: queueItem);
            await database.SaveChangesAsync();
        }
    }
}