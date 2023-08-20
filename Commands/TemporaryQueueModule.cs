using CCTavern.Database;
using CCTavern.Logger;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using DSharpPlus;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DSharpPlus.Interactivity.Extensions;

namespace CCTavern.Commands {
    internal class TemporaryQueueModule : BaseCommandModule {
        const int ITEMS_PER_PAGE = 10;

        public MusicBot Music { private get; set; }

        private ILogger _logger;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicPlayModule>();
                return _logger;
            }
        }

        [Command("playonce"), Aliases("po")]
        [Description("Play a song once using a search (**Disabled during development**)")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task PlayOnce(CommandContext ctx, [RemainingText] string search) {
            logger.LogInformation(TLE.MBPlay, "Play Music Once: " + search);

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
            Guild dbGuild = await db.GetOrCreateDiscordGuild(conn.Guild);
            GuildQueueItem trackItem;

            if (loadResult.LoadResultType == LavalinkLoadResultType.PlaylistLoaded) {
                long ticks = DateTime.Now.Ticks;
                byte[] bytes = BitConverter.GetBytes(ticks);
                string interactionId = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=');

                var singleButton = new DiscordButtonComponent(ButtonStyle.Success, $"single{interactionId}", "Add single song");
                var playlistButton = new DiscordButtonComponent(ButtonStyle.Danger, $"playlist{interactionId}", "Add playlist");

                var builder = new DiscordMessageBuilder()
                    .WithContent("You added a playlist, do you want to add the whole thing to the temporary queue?")
                    .AddComponents(singleButton, playlistButton);

                var buttonMessage = await ctx.RespondAsync(builder);

                var interactivity = ctx.Client.GetInteractivity();
                var result = await interactivity.WaitForButtonAsync(buttonMessage, TimeSpan.FromSeconds(30));

                if (result.TimedOut) {
                    await buttonMessage.DeleteAsync();
                    await ctx.RespondAsync("Track was not added to temporary queue, interactive buttons timed out. (30+ seconds no response).");
                    return;
                } else if (result.Result.Id == $"single{interactionId}") {
                    track = loadResult.Tracks.ElementAt(loadResult.PlaylistInfo.SelectedTrack);
                    await buttonMessage.DeleteAsync();
                } else if (result.Result.Id == $"playlist{interactionId}") {
                    var requestedBy = await db.GetOrCreateCachedUser(new Guild { Id = ctx.Guild.Id }, ctx.Member);

                    // Add all the tracks to the queue
                    var list = loadResult.Tracks.ToList();
                    await buttonMessage.DeleteAsync();
                    var addingMessage = await ctx.RespondAsync($"Adding `0`/`{list.Count()}` tracks to temporary queue...");

                    // Create the queue
                    var playlist = new TemporaryQueue.TemporaryPlaylist(loadResult.PlaylistInfo.Name, requestedBy.Id, new());

                    // Loop the tracks
                    for (int x = 0; x < list.Count(); x++) {
                        var lt = list[x];

                        trackItem = await MusicBot.CreateGuildQueueItem(db, dbGuild, lt, ctx.Channel, ctx.Member, null, 0);
                        playlist.Songs.Add(trackItem);

                        // If we are the first track and join event then start playing it.
                        if (x == 0 && isPlayEvent) {
                            // Delete old tracks if they are there.
                            if (Music.TemporaryTracks.ContainsKey(dbGuild.Id))
                                Music.TemporaryTracks.Remove(dbGuild.Id);

                            // Add the playlist
                            var tempQueue = new TemporaryQueue(dbGuild.Id);
                            tempQueue.SongItems.Add(playlist);

                            Music.TemporaryTracks.Add(dbGuild.Id, tempQueue);
                            tempQueue.IsPlaying = true;

                            await conn.PlayAsync(lt);
                            logger.LogInformation("Loading playlist, Playing first track.");
                        }

                        // Check if we dont have a temp queue.
                        if (!Music.TemporaryTracks.ContainsKey(dbGuild.Id)) {
                            // Create a temporary queue
                            var tempQueue = new TemporaryQueue(dbGuild.Id);
                            tempQueue.SongItems.Add(playlist);
                            Music.TemporaryTracks.Add(dbGuild.Id, tempQueue);
                        }

                        // We have a temp queue, lets make sure our playlist is in it.
                        else {
                            // Make sure we are still in the temp playlist
                            var tempTracks = Music.TemporaryTracks[dbGuild.Id];
                            if (tempTracks.SongItems.Contains(playlist))
                                tempTracks.SongItems.Add(playlist);
                        }

                        // Every 5 tracks update the index
                        if (x % 15 == 0)
                            await addingMessage.ModifyAsync($"Adding `{x}`/`{list.Count()}` tracks to temporary queue...");
                    }

                    // Now we are done processing check if the playlist still exists
                    if (playlist.PlaylistSongCount > 0) {
                        // Check if we dont have a temp queue.
                        if (!Music.TemporaryTracks.ContainsKey(dbGuild.Id)) {
                            // Create a temporary queue
                            var tempQueue = new TemporaryQueue(dbGuild.Id);
                            tempQueue.SongItems.Add(playlist);
                            Music.TemporaryTracks.Add(dbGuild.Id, tempQueue);
                        }

                        // We have a temp queue, lets make sure our playlist is in it.
                        else {
                            // Make sure we are still in the temp playlist
                            var tempTracks = Music.TemporaryTracks[dbGuild.Id];
                            if (tempTracks.SongItems.Contains(playlist))
                                tempTracks.SongItems.Add(playlist);
                        }
                    }

                    await addingMessage.ModifyAsync($"Successfully added `{list.Count()}` tracks to temporary queue...");
                    return;
                } else {
                    await buttonMessage.DeleteAsync();
                    await ctx.RespondAsync("Track was not added to temporary queue, unable to determine selected button.");
                    return;
                }
            } else {
                track = loadResult.Tracks.First();
            }

            trackItem = await MusicBot.CreateGuildQueueItem(db, dbGuild, track, ctx.Channel, ctx.Member, null, 0);

            if (!Music.TemporaryTracks.ContainsKey(dbGuild.Id)) {
                // Create a temporary queue
                var tempQueue = new TemporaryQueue(dbGuild.Id);
                tempQueue.SongItems.Add(new TemporaryQueue.TemporarySong(trackItem));
                Music.TemporaryTracks.Add(dbGuild.Id, tempQueue);
            } else {
                Music.TemporaryTracks[dbGuild.Id].SongItems.Add(new TemporaryQueue.TemporarySong(trackItem));
            }

            await ctx.RespondAsync($"Enqueued `{track.Title}` in *temporary* queue.");

            if (isPlayEvent) {
                Music.TemporaryTracks[dbGuild.Id].IsPlaying = true;
                await conn.PlayAsync(track);
                logger.LogInformation("Play Once Music: Playing song...");
            }
        }


        [Command("queuetemp"), Aliases("qtmp")]
        [Description("Lists all songs in the temporary queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task GetQueueTemporary(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            int Page = -1
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Check if the current guild has temporary music
            if (!Music.TemporaryTracks.ContainsKey(ctx.Guild.Id)) {
                await message.ModifyAsync($"Temporary queue is empty.");
                return;
            }

            var tempQueue = Music.TemporaryTracks[ctx.Guild.Id];

            // Get the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            var targetPage = 1;
            if (targetPage < 1) targetPage = 1;
            if (Page != -1) targetPage = Page;

            var guildQueueCount = tempQueue.SongCount;
            var pages = (int)Math.Ceiling(guildQueueCount / (double)ITEMS_PER_PAGE);
            targetPage = pages == 0 ? 0 : Math.Clamp(targetPage, 1, pages);

            if (guildQueueCount == 0) {
                queueContent += $"Temporary Queue Page 0 / 0 (0 songs)\n\n";
                queueContent += "  --- Queue is empty, enlist some songs or force a draft!";
                await message.ModifyAsync($"```{queueContent}```");
                return;
            }

            queueContent += $"Temporary Queue Page {targetPage} / {pages} ({guildQueueCount} songs)\n\n";

            List<GuildQueueItem> pageContents = new List<GuildQueueItem>();

            var songItemsTempList = tempQueue.SongItems.Take(ITEMS_PER_PAGE);
            foreach (var item in songItemsTempList) {
                if (item is TemporaryQueue.TemporaryPlaylist tp)
                    pageContents.AddRange(tp.Songs);

                else if (item is TemporaryQueue.TemporarySong ts)
                    pageContents.Add(ts.QueueItem);

                // Break if we reached our amount
                if (pageContents.Count >= ITEMS_PER_PAGE)
                    break;
            }

            pageContents = pageContents.Take(ITEMS_PER_PAGE).ToList();

            ulong? currentPlaylist = null;

            for (int x = 0; x < pageContents.Count(); x++) {
                var dbTrack = pageContents[x];

                GuildQueueItem? nextTrack = pageContents.ElementAtOrDefault(x + 1);

                if (dbTrack.PlaylistId == null) {
                    queueContent += " ";
                } else {
                    var lineSymbol =
                        (nextTrack != null && nextTrack.PlaylistId != dbTrack.PlaylistId)
                        || (dbTrack.Playlist.PlaylistSongCount == 1)
                        ? "/" : "|";

                    if (currentPlaylist == dbTrack.PlaylistId) {
                        queueContent += lineSymbol;
                    } else if (currentPlaylist != dbTrack.PlaylistId) {
                        queueContent += $"/ Playlist: {dbTrack.Playlist.Title} \n";

                        queueContent += lineSymbol;
                    } else if (currentPlaylist == null) {
                        queueContent += " ";
                    } else {
                        queueContent += " ";
                    }
                }

                currentPlaylist = dbTrack.PlaylistId;

                queueContent += " " + ((x == 0) ? "→" : " ");
                queueContent += $"{x,3}) ";
                queueContent += $"{dbTrack.Title} - Requested by ";

                var query = db.CachedUsers.Where(x => x.UserId == dbTrack.RequestedById && x.GuildId == guild.Id);
                CachedUser? requestedBy = null;

                if (await query.AnyAsync())
                    requestedBy = await query.FirstAsync();

                queueContent += (dbTrack.RequestedBy == null) ? "<#DELETED>" : $"{dbTrack.RequestedBy.Username}\n";
            }

            await message.ModifyAsync($"```{queueContent}```");
        }
    }
}
