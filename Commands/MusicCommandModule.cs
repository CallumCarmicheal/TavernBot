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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TimeSpanParserUtil;

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

        [Command("setMusicChannel"), Aliases("smc")]
        [Description("Set music channel for outputting messages")]
        public async Task SetMusicChannel(CommandContext ctx, DiscordChannel channel) {
            logger.LogInformation(TavernLogEvents.MBPlay, "Setting default music channel for {0} to {1}", channel.Guild.Name, channel.Name);

            var db = new TavernContext();

            Guild dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            dbGuild.MusicChannelId = channel.Id;
            dbGuild.MusicChannelName = channel.Name;
            await db.SaveChangesAsync();

            await ctx.RespondAsync($"Updated music output channel to <#{channel.Id}>.");
        }

        [Command("play"), Aliases("p")]
        [Description("Play music using a search")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Play(CommandContext ctx, [RemainingText] string search) {
            logger.LogInformation(TavernLogEvents.MBPlay, "Play Music: " + search);

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

            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            // Check if the bot is connected
            var conn = lava.GetGuildConnection(ctx.Member.VoiceState.Guild);
            var node = lava.ConnectedNodes.Values.First();
            bool isJoinEvent = false;

            if (conn == null) {
                // Connect the bot
                conn = await node.ConnectAsync(channel);
                Music.announceJoin(channel);

                await ctx.RespondAsync($"Connected to <#{channel.Id}>.");
                isJoinEvent = true;
            }

            LavalinkLoadResult loadResult;
            loadResult = Uri.TryCreate(search, UriKind.Absolute, out Uri? uri)
                ? await node.Rest.GetTracksAsync(uri)
                : await node.Rest.GetTracksAsync(search);

            // If something went wrong on Lavalink's end                          
            if (loadResult.LoadResultType == LavalinkLoadResultType.LoadFailed
                    //or it just couldn't find anything.
                    || loadResult.LoadResultType == LavalinkLoadResultType.NoMatches) {
                await ctx.RespondAsync($"Track search failed for {search}.");
                return;
            }

            LavalinkTrack track;

            if (loadResult.LoadResultType == LavalinkLoadResultType.PlaylistLoaded) {
                track = loadResult.Tracks.ElementAt(loadResult.PlaylistInfo.SelectedTrack);
            } else {
                track = loadResult.Tracks.First();
            }

            var trackPosition = await Music.enqueueMusicTrack(track, ctx.Channel, ctx.Member, isJoinEvent);
            await ctx.RespondAsync($"Enqueued `{track.Title}` in position `{trackPosition}`.");

            if (isJoinEvent) {
                var db = new TavernContext();
                var dbGuild = await db.GetOrCreateDiscordGuild(conn.Guild);
                dbGuild.CurrentTrack = trackPosition;
                await db.SaveChangesAsync();

                await conn.PlayAsync(track);
            }
        }

        [Command("join")]
        [Description("Join the current voice channel and do nothing.")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task JoinVoice(CommandContext ctx) {
            logger.LogInformation(TavernLogEvents.Misc, "Join voice");

            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var voiceState = ctx.Member?.VoiceState;
            if (voiceState == null) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            if (voiceState.Channel.GuildId != ctx.Guild.Id) {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return;
            }

            var channel = voiceState.Channel;
            var node = lava.ConnectedNodes.Values.First();

            if (channel.Type != ChannelType.Voice) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            await node.ConnectAsync(channel);
            Music.announceJoin(channel);

            await ctx.RespondAsync($"Joined <#{channel.Id}>!");
        }

        [Command("leave"), Aliases("quit", "stop")]
        [Description("Leaves the current voice channel")]
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
            Music.announceLeave(channel);

            await ctx.RespondAsync($"Left {channel.Name}!");
        }

        [Command("pause"), Aliases("-", "pp")]
        [Description("Pause currently playing track")]
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
                await ctx.RespondAsync("There are no tracks loaded.");
                return;
            }

            await conn.PauseAsync();
        }

        [Command("resume"), Aliases("r", "pr", "+")]
        [Description("Resume currently playing track")]
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
                await ctx.RespondAsync("There are no tracks loaded.");
                return;
            }

            await conn.ResumeAsync();
        }

        [Command("skip"), Aliases("s")]
        [Description("Skip currently playing track")]
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
                await ctx.RespondAsync("There are no tracks loaded.");
                return;
            }

            // Get the next track
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            var query = db.GuildQueueItems.Where(x => x.GuildId == ctx.Guild.Id && x.Position == guild.NextTrack);
            if (query.Any() == false) {
                await ctx.RespondAsync("Skipping... (There are no more tracks to play, add to the queue?)");
                await conn.StopAsync();
                return;
            }

            // Get the next song
            var dbTrack = await query.FirstAsync();
            
            var track = await node.Rest.DecodeTrackAsync(dbTrack.TrackString);
            if (track == null) {
                await ctx.RespondAsync($"Skipping... Error, Failed to parse next track `{dbTrack.Title}` at position `{dbTrack.Position}`.");
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
        public async Task Jump(CommandContext ctx, ulong nextTrackPosition) {
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

            var query = db.GuildQueueItems.Where(x => x.GuildId == ctx.Guild.Id && x.Position == nextTrackPosition);
            if (query.Any() == false) {
                await ctx.RespondAsync("Failed to jump to track (could not be found).");
                return;
            }

            // Get the next song
            var dbTrack = await query.FirstAsync();
            var track = await node.Rest.DecodeTrackAsync(dbTrack.TrackString);

            if (track == null) {
                await ctx.RespondAsync($"Jumping error, Failed to parse next track `{dbTrack.Title}` at position `{dbTrack.Position}`.");
                return;
            }

            track.TrackString = dbTrack.TrackString;

            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            await db.SaveChangesAsync();
            
            await conn.PlayAsync(track);
            await ctx.RespondAsync($"Jumped to track `{dbTrack.Title}` at position `{dbTrack.Position}`.");
        }

        [Command("nowplaying"), Aliases("np")]
        [Description("Current playing track")]
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
            await ctx.RespondAsync($"Currently playing `{dbTrack.Title}` at position `{dbTrack.Position}`.");
        }

        [Command("seek"), Aliases("fw", "sp", "ss")]
        [Description("Current playing track")]
        public async Task SeekPlayer(CommandContext ctx, string unparsedTimespan) {
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

            TimeSpan? timespan = _attemptToParseTimespan(unparsedTimespan);

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
    }
}
