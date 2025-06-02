using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using Humanizer;

using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
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
        private readonly ILogger<MusicCommandModule> logger;
        
        public MusicCommandModule(MusicBotHelper mbHelper, ILogger<MusicCommandModule> logger, IAudioService audioService) 
                : base(audioService, mbHelper) {
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

            (var playerState, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, ctx.Member?.VoiceState.Channel.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            if (playerIsConnected && playerState.Player != null) {
                await playerState.Player.DisconnectAsync().ConfigureAwait(false);
                await ctx.RespondAsync($"Left <#{ctx?.Member?.VoiceState.Channel.Id}>!");

                if (ctx?.Guild != null) 
                    mbHelper.AnnounceLeave(ctx.Guild.Id);
                
                // Dispose the bot
                await playerState.Player.DisposeAsync();
            } else {
                await ctx.RespondAsync("Music bot is not connected.");
            }
        }

        [Command("pause"), Aliases("-", "pp")]
        [Description("Pause currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Pause(CommandContext ctx) {
            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerState, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            // Check if we are connected
            if (playerIsConnected && playerState.Player != null) {
                // Check if we are playing any music currently
                if (playerState.Player.State != PlayerState.Playing) {
                    await ctx.RespondAsync("Music bot not currently playing any music.");
                } else {
                    // Pause the music bot
                    await playerState.Player.PauseAsync().ConfigureAwait(false);

                    DiscordEmoji? emoji = DiscordEmoji.FromName(ctx.Client, ":pause_button:");
                    await ctx.Message.CreateReactionAsync(emoji);
                    return;
                }
            } else {
                await ctx.RespondAsync("Music bot is not connected.");
            }
        }

        [Command("resume"), Aliases("r", "pr", "+")]
        [Description("Resume currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Resume(CommandContext ctx) {
            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerState, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            // Check if we are connected
            if (playerIsConnected && playerState.Player != null) {
                // Check if we are playing any music currently
                if (playerState.Player.State != PlayerState.Paused) {
                    await ctx.RespondAsync("Music bot not currently paused.");
                } else {
                    // Resume the music bot
                    await playerState.Player.ResumeAsync().ConfigureAwait(false);

                    DiscordEmoji? emoji = DiscordEmoji.FromName(ctx.Client, ":arrow_forward:");
                    await ctx.Message.CreateReactionAsync(emoji);
                    return;
                }
            } else {
                await ctx.RespondAsync("Music bot is not connected.");
            }
        }

        [Command("continue"), Aliases("c")]
        [Description("Continue currently playing the current track, if not playing anything play the next track in the queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Continue(CommandContext ctx) {
            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerState, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            // Check if we are connected
            if (playerIsConnected == false || playerState.Player == null) {
                await ctx.RespondAsync("Music bot is not connected.");
                return;
            }

            // Check if we are playing any music currently
            if (playerState.Player.State == PlayerState.Playing) {
                await ctx.RespondAsync("Music bot is already playing music. Did you mean to `skip`?");
                return;
            }
            // If the music bot is paused then resume it.
            else if (playerState.Player.State == PlayerState.Paused) {
                await playerState.Player.ResumeAsync().ConfigureAwait(false);

                var emoji = DiscordEmoji.FromName(ctx.Client, ":arrow_forward:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // The music bot is not playing anything
            // Get the next song and attempt to play it.
            var dbTrack = await mbHelper.getNextTrackForGuild(ctx.Guild);
            if (dbTrack != null) {
                await _jump_internal(ctx, dbTrack.Position, "Resumed playlist, playing track");
                return;
            }

            await ctx.RespondAsync("Unable retrieve next track, maybe you are at the end of the playlist?");
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

            // Parse the track
            var track = LavalinkTrack.Parse(dbTrack.TrackString, provider: null);
            if (track == null) {
                await ctx.RespondAsync($"Skipping... Error, Failed to parse next track {await dbTrack.GetTagline(db, true)}.");
                return;
            }

            // Update the guild queue location.
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

            (var playerQuery, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);
            if (playerIsConnected == false || playerQuery.Player == null) {
                await ctx.RespondAsync("Music bot is not connected.");
                return;
            }

            // Get the next track
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            var query = db.GuildQueueItems.Where(x => x.Position == guild.CurrentTrack);
            if (query.Any() == false) {
                await ctx.RespondAsync("Failed to jump to track (could not be found).");
                return;
            }

            // Get the current song
            var dbTrack = await query.FirstAsync();
            await ctx.RespondAsync($"Currently playing {await dbTrack.GetTagline(db, true)}.");
        }

        [Command("seek"), Aliases("fw", "sp", "ss")]
        [Description("Current playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task SeekPlayer(CommandContext ctx,
            [RemainingText]
            [Description("Timespan to parse, examples \"1hr 30m 20s\", \"33 seconds\", \"minute:second\", \"hour:minute:second\"")]
            string unparsedTimespan
        ) {
            DiscordEmoji? emoji = null;

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

            // Check if we dont have a track
            if (playerQuery.Player.CurrentTrack == null) {
                emoji = DiscordEmoji.FromName(ctx.Client, ":face_with_raised_eyebrow:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // Attempt to parse the timespan
            TimeSpan? timespan = unparsedTimespan.TryParseTimeStamp();
            if (timespan == null) {
                emoji = DiscordEmoji.FromName(ctx.Client, ":question:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // Seek the player
            await playerQuery.Player.SeekAsync(timespan.Value);
            await ctx.RespondAsync($"Seeked to `{timespan.Value.Humanize()}`.");
        }

        [Command("random"), Aliases("rnd", "rj", "randomjump")]
        [Description("Current playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task RandomJump(CommandContext ctx,
            [Description("If the song should instantly be played or play after the current one")]
            bool replaceCurrentTrack = true, 
            [Description("Start position to use when generating a random number")]
            int startPosition = -1, 
            [Description("Last position to use when generating a random number")]
            int endPosition = -1
        ) {
            // Check if the user is in the voice channel
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerQuery, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            // If the player is not connected, connect the music bot.
            if (playerIsConnected == false || playerQuery.Player == null) {
                // Connect the music bot
                (playerQuery, playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, ctx.Member?.VoiceState.Channel.Id, connectToVoiceChannel: true).ConfigureAwait(false);

                if (playerIsConnected == false || playerQuery.Player == null) {
                    await ctx.RespondAsync("Failed to start bot, please try again later.");
                    return;
                }

                await ctx.RespondAsync($"Connected to <#{ctx.Member?.VoiceState.Channel.Id}>.");
            }

            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Check if we have tracks in the queue
            var guildQueueQuery = db.GuildQueueItems
                .Include(x => x.RequestedBy)
                .Where(x => x.GuildId == guild.Id && x.IsDeleted == false)
                .OrderBy(x => x.Position);

            if (await guildQueueQuery.AnyAsync() == false) {
                await ctx.RespondAsync("Sorry bro, there ain't nothing te shuffle. Try adding some songs first.");
                return;
            }
            
            // Update the random positions
            if (startPosition == -1) startPosition = 0;
            if (endPosition == -1)   endPosition = guildQueueQuery.Count();

            // Get a random track from the database
            var rand = new Random();
            var dbTrack = await guildQueueQuery.Skip(rand.Next(startPosition, endPosition)).FirstAsync();

            // If we failed to find one using the Position, try again
            if (dbTrack == null)
                // attempt to do it one more time
                dbTrack = await guildQueueQuery.Skip(rand.Next(startPosition, endPosition)).FirstAsync();

            // TODO: Maybe find the nearest track using the position above.
            if (dbTrack == null) {
                await ctx.RespondAsync("***Bot got hurt in the confusion***. For some reason or another (ᴾʳᵒᵇᵃᵇˡʸ ᶜᵃˡˡᵘᵐˢ ᵇᵃᵈ ᵖʳᵒᵍʳᵃᵐᵐᶦⁿᵍ)"
                    + " I cant find a track to play. Try again... maybe!?");
                return;
            }

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack    = dbTrack.Position + 1;

            // Parse the track from the database
            var track = LavalinkTrack.Parse(dbTrack.TrackString, provider: null);
            if (track == null) {
                await ctx.RespondAsync($"Jumping error, Failed to parse next track {await dbTrack.GetTagline(db, true)}");
                return;
            }

            // If we are playing the track 
            if (replaceCurrentTrack) {
                guild.CurrentTrack = dbTrack.Position;
                guild.NextTrack    = dbTrack.Position + 1;
                await db.SaveChangesAsync();

                // Swap the track
                await playerQuery.Player.PlayAsync(track);
                await ctx.RespondAsync($"Jumped to track {await dbTrack.GetTagline(db, true)}.");
            } 
            else {
                // Update the next track
                guild.NextTrack = dbTrack.Position;
                await db.SaveChangesAsync();

                await ctx.RespondAsync($"Next track set to {await dbTrack.GetTagline(db, true)}.");
            }
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
