using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Tracks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Org.BouncyCastle.Asn1.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

using TimeSpanParserUtil;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace CCTavern.Commands
{
    internal class MusicCommandModule : BaseAudioCommandModule {
        private readonly MusicBotHelper mbHelper;
        private readonly ILogger<MusicCommandModule> logger;
        
        public MusicCommandModule(MusicBotHelper bot, ILogger<MusicCommandModule> logger, IAudioService audioService) : base(audioService) {
            this.mbHelper = bot;
            this.logger = logger;
        }

        [Command("leave"), Aliases("quit", "stop")]
        [Description("Leaves the current voice channel")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Leave(CommandContext ctx) {
            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerState, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            if (playerIsConnected && playerState.Player != null) {
                await playerState.Player.DisconnectAsync().ConfigureAwait(false);

                await ctx.RespondAsync($"Left <#{ctx.Member.VoiceState.Channel.Id}>!");
            } else {
                await ctx.RespondAsync("Music bot is not connected.");
            }
        }

        [Command("pause"), Aliases("-", "pp")]
        [Description("Pause currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Pause(CommandContext ctx) {
            await ctx.RespondAsync($"TODO: Not implemented!");
            throw new NotImplementedException(); // TODO: Implement
        }

        [Command("resume"), Aliases("r", "pr", "+")]
        [Description("Resume currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Resume(CommandContext ctx) {
            await ctx.RespondAsync($"TODO: Not implemented!");
            throw new NotImplementedException(); // TODO: Implement
        }

        [Command("continue"), Aliases("c")]
        [Description("Continue currently playing playing")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Continue(CommandContext ctx) {
            await ctx.RespondAsync($"TODO: Not implemented!");
            throw new NotImplementedException(); // TODO: Implement
        }

        [Command("skip"), Aliases("s")]
        [Description("Skip currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Skip(CommandContext ctx) {
            // Check if the user is in the voice channel
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerQuery, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            if (playerIsConnected == false || playerQuery.Player == null) {
                await ctx.RespondAsync("Music bot is not connected.");
                return;
            }

            var player = playerQuery.Player;

            // Disabled: No need to check if we are playing anything, just play the next track.
            //if (playerQuery.Player.State == Lavalink4NET.Players.PlayerState.NotPlaying || playerQuery.Player.CurrentTrack == null) {
            //    await ctx.RespondAsync("There are no tracks playing.");
            //    return;
            //}

            // Get the next track
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Get the next song
            var dbTrack = await mbHelper.getNextTrackForGuild(ctx.Guild);

            if (dbTrack == null) {
                await ctx.RespondAsync("Skipping... (There are no more tracks to play, add to the queue?)");
                await player.StopAsync().ConfigureAwait(false);
                return;
            }

            var track = LavalinkTrack.Parse(dbTrack.TrackString, provider: null);
            if (track == null) {
                await ctx.RespondAsync($"Skipping... Error, Failed to parse next track {await dbTrack.GetTagline(db, true)}.");
                return;
            }

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            await db.SaveChangesAsync();
            await ctx.RespondAsync("Skipped track.");

            logger.LogDebug("Skipping, play async");
            await player.PlayAsync(track).ConfigureAwait(false);
            logger.LogDebug("Skipping done.");
        }

        [Command("jump"), Aliases("j")]
        [Description("Jump to track position")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Jump(CommandContext ctx,
            [Description("Track poisiton to jump to")]
            ulong nextTrackPosition
        ) {
            await _jump_internal(ctx, nextTrackPosition);
        }

        private async Task _jump_internal(CommandContext ctx,
            ulong nextTrackPosition,
            string jumpedPrefixTitle = "Jumped to track"
        ) {
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerState, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);
            if (playerIsConnected == false || playerState.Player == null) {
                DiscordEmoji? emoji = DiscordEmoji.FromName(ctx.Client, ":face_with_raised_eyebrow:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // Get the next track
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            var query = db
                .GuildQueueItems.Include(x => x.RequestedBy)
                .Where(x => x.GuildId == ctx.Guild.Id && x.Position == nextTrackPosition && x.IsDeleted == false);
            if (query.Any() == false) {
                await ctx.RespondAsync("Failed to jump to track (could not be found).");
                return;
            }

            // Get the next song
            var dbTrack = await query.FirstAsync();
            var track = LavalinkTrack.Parse(dbTrack.TrackString, provider: null);
            if (track == null) {
                await ctx.RespondAsync($"Jumping error, Failed to parse next track {await dbTrack.GetTagline(db, true)}.");
                return;
            }

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            await db.SaveChangesAsync();

            await playerState.Player.PlayAsync(track).ConfigureAwait(false);
            await ctx.RespondAsync($"{jumpedPrefixTitle} {await dbTrack.GetTagline(db, true)}.");
        }

        [Command("nowplaying"), Aliases("np")]
        [Description("Current playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task NowPlaying(CommandContext ctx) {
            throw new NotImplementedException(); // TODO: Implement
        }

        [Command("seek"), Aliases("fw", "sp", "ss")]
        [Description("Current playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task SeekPlayer(CommandContext ctx,
            [Description("Timespan to parse, examples \"1hr 30m 20s\", \"33 seconds\", \"minute:second\", \"hour:minute:second\"")]
            string unparsedTimespan
        ) {
            await ctx.RespondAsync($"TODO: Not implemented!");
            throw new NotImplementedException(); // TODO: Implement
        }

        [Command("random"), Aliases("rnd", "rj", "randomjump")]
        [Description("Current playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task RandomJump(CommandContext ctx,
            [Description("If the song should instantly be played or play after the current one")]
            bool playInstant = true, 
            [Description("Start position to use when generating a random number")]
            int startPosition = -1, 
            [Description("Last position to use when generating a random number")]
            int endPosition = -1
        ) {
            await ctx.RespondAsync($"TODO: Not implemented!");
            throw new NotImplementedException(); // TODO: Implement
        }

        private TimeSpan? _attemptToParseTimespan(string input) {
            TimeSpan ts;

            if (TimeSpan.TryParseExact(input, new string[] { "ss", "mm\\:ss", "mm\\-ss", "mm\\'ss", "mm\\;ss" }, null, out ts))
                return ts;

            if (TimeSpanParser.TryParse(input, timeSpan: out ts))
                return ts;

            return null;
        }
    }
}
