using CCTavern;
using CCTavern.Commands.Test;
using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;

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

namespace CCTavern.Commands {
    internal class MusicCommandModule : BaseCommandModule {
        public MusicBot Music { private get; set; }

        private ILogger _logger;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicCommandModule>();
                return _logger;
            }
        }

        [Command("test")]
        public void Test(CommandContext ctx) {
            var member = ctx.Member;
        }

        [Command("leave"), Aliases("quit", "stop")]
        [Description("Leaves the current voice channel")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Leave(CommandContext ctx) {
            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var node = lava.ConnectedNodes.Values.First();

            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var voiceState = ctx.Member.VoiceState;
            var channel = voiceState.Channel;

            var conn = node?.GetGuildConnection(channel.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            await conn.DisconnectAsync();
            MusicBot.AnnounceLeave(channel);

            await ctx.RespondAsync($"Left {channel.Name}!");
        }

        [Command("pause"), Aliases("-", "pp")]
        [Description("Pause currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Pause(CommandContext ctx) {
            if (ctx.Member?.VoiceState == null || ctx.Member?.VoiceState?.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            if (conn.CurrentState.CurrentTrack == null) {
                await ctx.RespondAsync("There are no tracks playing.");
                return;
            }

            await conn.PauseAsync();
        }

        [Command("resume"), Aliases("r", "pr", "+")]
        [Description("Resume currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Resume(CommandContext ctx) {
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            if (conn.CurrentState.CurrentTrack == null) {
                await ctx.RespondAsync("There are no tracks playing.");
                return;
            }

            await conn.ResumeAsync();
        }

        [Command("continue"), Aliases("c")]
        [Description("Continue currently playing playing")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Continue(CommandContext ctx) {
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            if (conn.CurrentState.CurrentTrack == null) {
                // Get the next track
                var db = new TavernContext();
                var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

                // Get the next song
                var dbTrack = await Music.getNextTrackForGuild(ctx.Guild);
                if (dbTrack != null) {
                    await _jump_internal(ctx, dbTrack.Position, "Resumed playlist, playing track");
                    return;
                }

                await ctx.RespondAsync("Unable retrieve next track, maybe you are at the end of the playlist?");
            } else {
                await ctx.RespondAsync("There is already music playing.");
            }
        }

        [Command("skip"), Aliases("s")]
        [Description("Skip currently playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Skip(CommandContext ctx) {
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null || node == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            if (conn.CurrentState.CurrentTrack == null) {
                await ctx.RespondAsync("There are no tracks playing.");
                return;
            }

            // Get the next track
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Get the next song
            var dbTrack = await Music.getNextTrackForGuild(ctx.Guild);

            if (dbTrack == null) {
                await ctx.RespondAsync("Skipping... (There are no more tracks to play, add to the queue?)");
                await conn.StopAsync();
                return;
            }
            
            var track = await node.Rest.DecodeTrackAsync(dbTrack.TrackString);
            if (track == null) {
                await ctx.RespondAsync($"Skipping... Error, Failed to parse next track {await dbTrack.GetTagline(db, true)}.");
                return;
            }

            // Fix the track 
            track.TrackString = dbTrack.TrackString;

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack    = dbTrack.Position + 1;
            await db.SaveChangesAsync();
            await ctx.RespondAsync("Skipped.");
            
            logger.LogDebug("Skipping, play async");
            await conn.PlayAsync(track);
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

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null || node == null) {
                //await ctx.RespondAsync("Lavalink is not connected.");
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
            var track = await node.Rest.DecodeTrackAsync(dbTrack.TrackString);

            if (track == null) {
                await ctx.RespondAsync($"Jumping error, Failed to parse next track {await dbTrack.GetTagline(db, true)}.");
                return;
            }

            track.TrackString = dbTrack.TrackString;

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            await db.SaveChangesAsync();

            await conn.PlayAsync(track);
            await ctx.RespondAsync($"{jumpedPrefixTitle} {await dbTrack.GetTagline(db, true)}.");
        }

        [Command("nowplaying"), Aliases("np")]
        [Description("Current playing track")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task NowPlaying(CommandContext ctx) {
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                //await ctx.RespondAsync("Lavalink is not connected.");
                DiscordEmoji? emoji = DiscordEmoji.FromName(ctx.Client, ":face_with_raised_eyebrow:");
                await ctx.Message.CreateReactionAsync(emoji);
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
            [Description("Timespan to parse, examples \"1hr 30m 20s\", \"33 seconds\", \"minute:second\", \"hour:minute:second\"")]
            string unparsedTimespan
        ) {
            DiscordEmoji? emoji = null;

            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                //await ctx.RespondAsync("You are not in a voice channel.");
                emoji = DiscordEmoji.FromName(ctx.Client, ":middle_finger:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member.VoiceState.Guild);

            // Not connected
            if (conn == null) {
                //await ctx.RespondAsync("Lavalink is not connected.");
                emoji = DiscordEmoji.FromName(ctx.Client, ":face_with_raised_eyebrow:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // Not playing anything
            if (conn.CurrentState.CurrentTrack == null) {
                emoji = DiscordEmoji.FromName(ctx.Client, ":face_with_raised_eyebrow:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // Check if we are playing something
            //if (conn.CurrentState == 

            TimeSpan? timespan = unparsedTimespan.TryParseTimeStamp();

            if (timespan == null) {
                emoji = DiscordEmoji.FromName(ctx.Client, ":question:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            await conn.SeekAsync(timespan.Value);

            emoji = DiscordEmoji.FromName(ctx.Client, ":white_check_mark:");
            await ctx.Message.CreateReactionAsync(emoji);
            //await ctx.RespondAsync(timespan.Value.ToString());
        }

        private TimeSpan? _attemptToParseTimespan(string input) {
            TimeSpan ts;
                
            if (TimeSpan.TryParseExact(input, new string[] { "ss", "mm\\:ss", "mm\\-ss", "mm\\'ss", "mm\\;ss" }, null, out ts)) 
                return ts;

            if (TimeSpanParser.TryParse(input, timeSpan: out ts))
                return ts;

            return null;
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
            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node?.GetGuildConnection(ctx.Member?.VoiceState.Guild);

            if (node == null) {
                await ctx.RespondAsync("I am not connected to any voice servers, I cannot play any music at this moment.");
                return;
            }

            if (conn == null) {
                // Check if we have a valid voice state
                if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                    await ctx.RespondAsync("You are not in a voice channel.");
                    return;
                }

                var voiceState = ctx.Member.VoiceState;
                var channel = voiceState.Channel;
                if (voiceState.Channel.GuildId != ctx.Guild.Id) {
                    await ctx.RespondAsync("Not in voice channel of this guild.");
                    return;
                }
                if (channel.Type != ChannelType.Voice) {
                    await ctx.RespondAsync("Impossible error but I dunno we got here somehow, Not a valid voice channel.");
                    return;
                }

                // Connect the bot
                conn = await node.ConnectAsync(channel);
                MusicBot.AnnounceJoin(channel);

                await ctx.RespondAsync($"Connected to <#{channel.Id}>.");
            }

            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Check if we have tracks in the queue
            var guildQueueQuery = db.GuildQueueItems
                .Include(x => x.RequestedBy)
                .Where  (x => x.GuildId == guild.Id && x.IsDeleted == false)
                .OrderBy(x => x.Position);
            if (await guildQueueQuery.AnyAsync() == false) {
                await ctx.RespondAsync("Sorry bro, there ain't nothing te shuffle. Try adding some songs first.");
                return;
            }

            var rand = new Random();
            var dbTrack = await guildQueueQuery.Skip(rand.Next(guildQueueQuery.Count())).FirstAsync();

            if (dbTrack == null) 
                // attempt to do it one more time
                dbTrack = await guildQueueQuery.Skip(rand.Next(guildQueueQuery.Count())).FirstAsync();

            if (dbTrack == null) {
                await ctx.RespondAsync("***Bot got hurt in the confusion***. For some reason or another (ᴾʳᵒᵇᵃᵇˡʸ ᶜᵃˡˡᵘᵐˢ ᵇᵃᵈ ᵖʳᵒᵍʳᵃᵐᵐᶦⁿᵍ)" 
                    + " I cant find a track to play. Try again... maybe!?");
                return;
            }

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            guild.IsPlaying = true;

            var track = await node.Rest.DecodeTrackAsync(dbTrack.TrackString);

            if (track == null) {
                await ctx.RespondAsync($"Jumping error, Failed to parse next track {await dbTrack.GetTagline(db, true)}");
                return;
            }

            track.TrackString = dbTrack.TrackString;
            if (playInstant) guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            await db.SaveChangesAsync();

            await conn.PlayAsync(track);
            await ctx.RespondAsync($"Jumped to track {await dbTrack.GetTagline(db, true)}.");
        }
    }
}
