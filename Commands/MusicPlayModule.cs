using Azure.Identity;

using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Lavalink;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class MusicPlayModule : BaseCommandModule {

        public MusicBot Music { private get; set; }

        private ILogger _logger;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicPlayModule>();
                return _logger;
            }
        }


        [Command("play"), Aliases("p")]
        [Description("Play music using a search")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Play(CommandContext ctx, [RemainingText] string search) {
            logger.LogInformation(TLE.MBPlay, "Play Music: " + search);

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
            bool isPlayEvent = false;

            if (conn == null) {
                // Connect the bot
                conn = await node.ConnectAsync(channel);
                Music.announceJoin(channel);

                await ctx.RespondAsync($"Connected to <#{channel.Id}>.");
                isPlayEvent = true;
            }

            isPlayEvent = isPlayEvent ? true : conn.CurrentState.CurrentTrack == null;

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
            var db = new TavernContext();

            if (loadResult.LoadResultType == LavalinkLoadResultType.PlaylistLoaded) {
                long ticks = DateTime.Now.Ticks;
                byte[] bytes = BitConverter.GetBytes(ticks);
                string interactionId = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=');

                var singleButton = new DiscordButtonComponent(ButtonStyle.Success, $"single{interactionId}", "Add single song");
                var playlistButton = new DiscordButtonComponent(ButtonStyle.Danger, $"playlist{interactionId}", "Add Playlist");

                var builder = new DiscordMessageBuilder()
                    .WithContent("You added a playlist, do you want to add the whole thing?")
                    .AddComponents(singleButton, playlistButton);

                var buttonMessage = await ctx.RespondAsync(builder);

                var interactivity = ctx.Client.GetInteractivity();
                var result = await interactivity.WaitForButtonAsync(buttonMessage, TimeSpan.FromSeconds(30));

                if (result.TimedOut) {
                    await buttonMessage.DeleteAsync();
                    await ctx.RespondAsync("Track was not added to queue, interactive buttons timed out. (30+ seconds no response).");
                    return;
                } 
                else if (result.Result.Id == $"single{interactionId}") {
                    track = loadResult.Tracks.ElementAt(loadResult.PlaylistInfo.SelectedTrack);
                    await buttonMessage.DeleteAsync();
                } 
                else if (result.Result.Id == $"playlist{interactionId}") {
                    var dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);
                    var requestedBy = await db.GetOrCreateCachedUser(new Guild { Id = ctx.Guild.Id }, ctx.Member);

                    // Add all the tracks to the queue
                    var list = loadResult.Tracks.ToList();
                    await buttonMessage.DeleteAsync();
                    var addingMessage = await ctx.RespondAsync($"Adding `0`/`{list.Count()}` tracks to playlist...");

                    // Create the queue 
                    var playlist = new GuildQueuePlaylist();
                    playlist.Title = loadResult.PlaylistInfo.Name;
                    playlist.CreatedById = requestedBy.Id;
                    playlist.PlaylistSongCount = list.Count();
                    db.GuildQueuePlaylists.Add(playlist);
                    await db.SaveChangesAsync();

                    // Loop the tracks
                    for (int x = 0; x < list.Count(); x++) {
                        var lt = list[x];
                        var trackIdx = await Music.enqueueMusicTrack(lt, ctx.Channel, ctx.Member, playlist, (x == 0 && isPlayEvent));

                        // If we are the first track and join event then start playing it.
                        if (x == 0 && isPlayEvent) {
                            dbGuild = await db.GetOrCreateDiscordGuild(conn.Guild);
                            dbGuild.CurrentTrack = trackIdx;
                            await db.SaveChangesAsync();
                            await conn.PlayAsync(lt);
                            logger.LogInformation("Loading playlist, Playing first track.");
                        }

                        // Every 5 tracks update the index
                        if (x % 15 == 0) 
                            await addingMessage.ModifyAsync($"Adding `{x}`/`{list.Count()}` tracks to playlist...");
                    }
                    
                    await addingMessage.ModifyAsync($"Successfully added `{list.Count()}` tracks to playlist...");
                    return;
                } 
                else {
                    await buttonMessage.DeleteAsync();
                    await ctx.RespondAsync("Track was not added to queue, unable to determine selected button.");
                    return;
                }
            } else {
                track = loadResult.Tracks.First();
            }

            var trackPosition = await Music.enqueueMusicTrack(track, ctx.Channel, ctx.Member, null, isPlayEvent);
            await ctx.RespondAsync($"Enqueued `{track.Title}` in position `{trackPosition}`.");

            if (isPlayEvent) {
                var dbGuild = await db.GetOrCreateDiscordGuild(conn.Guild);
                dbGuild.CurrentTrack = trackPosition;
                await db.SaveChangesAsync();

                await conn.PlayAsync(track);
                logger.LogInformation("Play Music: Playing song...");
            }
        }


        [Command("playonce"), Aliases("p")]
        [Description("Play a song once using a search")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task PlayOnce(CommandContext ctx, [RemainingText] string search) {

        }
    }
}
