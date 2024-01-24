using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DSharpPlus.Net;
using DSharpPlus.Lavalink;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using CCTavern.Logger;
using DSharpPlus.Entities;
using CCTavern.Database;
using Microsoft.EntityFrameworkCore;
using System.Web;
using DSharpPlus.CommandsNext;

namespace CCTavern {
    public class MusicBot {
        private readonly DiscordClient client;
        private readonly ILogger logger;


        public LavalinkExtension Lavalink { get; private set; }

        private ConnectionEndpoint? _lavalinkEndpoint = null;
        private LavalinkConfiguration? _lavalinkConfiguration = null;

        internal Dictionary<ulong, TemporaryQueue> TemporaryTracks = new Dictionary<ulong, TemporaryQueue>();
        internal static Dictionary<ulong, GuildState> GuildStates { get; set; } = new Dictionary<ulong, GuildState>();

        public MusicBot(DiscordClient client) {
            this.client = client;
            this.logger = Program.LoggerFactory.CreateLogger("MusicBot");
        }

        public async Task SetupLavalink() {
            logger.LogInformation(TLE.MBSetup, "Setting up lavalink");

            var lavalinkSettings = Program.Settings.Lavalink;

            _lavalinkEndpoint = new ConnectionEndpoint {
                Hostname = lavalinkSettings.Hostname,
                Port = lavalinkSettings.Port
            };

            _lavalinkConfiguration = new LavalinkConfiguration {
                Password = lavalinkSettings.Password,
                RestEndpoint = _lavalinkEndpoint.Value,
                SocketEndpoint = _lavalinkEndpoint.Value
            };

            Lavalink = client.UseLavalink();
            var lavalinkNode = await Lavalink.ConnectAsync(_lavalinkConfiguration);

            lavalinkNode.PlayerUpdated += LavalinkNode_PlayerUpdated;
            lavalinkNode.PlaybackStarted += LavalinkNode_PlaybackStarted;
            lavalinkNode.PlaybackFinished += LavalinkNode_PlaybackFinished;
            lavalinkNode.TrackException += LavalinkNode_TrackException;
            lavalinkNode.TrackStuck += LavalinkNode_TrackStuck;
            lavalinkNode.Disconnected += LavalinkNode_Disconnected;

            logger.LogInformation(TLE.MBSetup, "Lavalink successful");
        }

        public static async Task<DiscordChannel?> GetMusicTextChannelFor(DiscordGuild guild) {
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(guild);

            // Check if we have that in the database
            ulong? discordChannelId = null;

            if (dbGuild.MusicChannelId == null) {
                if (GuildStates.ContainsKey(guild.Id) && GuildStates[guild.Id].TemporaryMusicChannelId != null)
                    discordChannelId = GuildStates[guild.Id].TemporaryMusicChannelId;
            } else discordChannelId = dbGuild.MusicChannelId;

            if (discordChannelId == null) return null;
            return await Program.Client.GetChannelAsync(discordChannelId.Value);
        }

        public static void AnnounceJoin(DiscordChannel channel) {
            if (channel == null) return;

            if (!GuildStates.ContainsKey(channel.Guild.Id))
                GuildStates[channel.Guild.Id] = new GuildState(channel.Guild.Id);

            if (GuildStates[channel.Guild.Id].TemporaryMusicChannelId == null)
                GuildStates[channel.Guild.Id].TemporaryMusicChannelId = channel.Id;
        }

        public static void AnnounceLeave(DiscordChannel channel) {
            if (channel == null) return;

            if (GuildStates.ContainsKey(channel.Guild.Id))
                GuildStates.Remove(channel.Guild.Id);
        }

        public static async Task<GuildQueueItem> CreateGuildQueueItem(
                TavernContext db, Guild dbGuild,
                LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, ulong trackPosition
        ) {
            var requestedUser = await db.GetOrCreateCachedUser(dbGuild, requestedBy);
            var qi = new GuildQueueItem() {
                GuildId = channel.Guild.Id,
                Length = track.Length,
                Position = trackPosition,
                RequestedById = requestedUser.Id,
                Title = track.Title,
                TrackString = track.TrackString,
                PlaylistId = playlist?.Id
            };

            return qi;
        }

        public async Task<ulong> enqueueMusicTrack(LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, bool updateNextTrack) {
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(channel.Guild);

            dbGuild.TrackCount = dbGuild.TrackCount + 1;
            var trackPosition = dbGuild.TrackCount;
            await db.SaveChangesAsync();

            logger.LogInformation(TLE.Misc, $"Queue Music into {channel.Guild.Name}.{channel.Name} [{trackPosition}] from {requestedBy.Username}: {track.Title}, {track.Length.ToString(@"hh\:mm\:ss")}");

            var qi = await CreateGuildQueueItem(db, dbGuild, track, channel, requestedBy, playlist, trackPosition);

            if (updateNextTrack) {
                dbGuild.NextTrack = trackPosition + 1;
                logger.LogInformation(TLE.Misc, $"Setting next track to current position.");
            }

            db.GuildQueueItems.Add(qi);
            await db.SaveChangesAsync();

            return trackPosition;
        }

        public async Task<GuildQueueItem?> getNextTrackForGuild(DiscordGuild discordGuild, ulong? targetTrackId = null) {
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(discordGuild);

            if (GuildStates.ContainsKey(discordGuild.Id)) {
                var guildState = GuildStates[discordGuild.Id];

                if (guildState != null && guildState.ShuffleEnabled) {
                    var guildQueueQuery = db.GuildQueueItems
                        .Where(x => x.GuildId == guild.Id && x.IsDeleted == false)
                        .OrderByDescending(x => x.Position);

                    if (await guildQueueQuery.CountAsync() >= 10) {
                        var rnd = new Random();

                        var largestTrackNumber = await guildQueueQuery.Select(x => x.Position).FirstOrDefaultAsync();
                        targetTrackId = Convert.ToUInt64(rnd.ULongNext(0, largestTrackNumber));
                    } else {
                        if (guildState != null) guildState.ShuffleEnabled = false;
                    }
                }
            }

            targetTrackId ??= guild.NextTrack;

            var query = db.GuildQueueItems
                .Include(x => x.RequestedBy)
                .Where(
                    x => x.GuildId == guild.Id
                      && x.Position >= targetTrackId
                      && x.IsDeleted == false);

            if (query.Any() == false)
                return null;

            return await query.FirstAsync();
        }

        public (bool isTempTrack, GuildQueueItem? dbTrack) GetNextTemporaryTrackForGuild(Guild guild) {
            bool isTempTrack = false;
            GuildQueueItem? dbTrack = null;

            if (TemporaryTracks.ContainsKey(guild.Id)) {
                if (Program.Settings.LoggingVerbose) logger.LogError(TLE.MBFin, "Temporary tarcks discovered");

                var tempTracks = TemporaryTracks[guild.Id];
                tempTracks.SongItems.FirstOrDefault()?.FinishedPlaying(tempTracks);

                // Remove empty queue
                if (tempTracks.SongItems.Any() == false || tempTracks.SongItems.FirstOrDefault() == null) {
                    if (Program.Settings.LoggingVerbose) logger.LogError(TLE.MBFin, "Removing empty temporary track queue.");

                    TemporaryTracks.Remove(guild.Id);
                }

                // Get the track
                else {
                    dbTrack = tempTracks.SongItems.First()?.GetQueueItem();
                    isTempTrack = dbTrack != null;

                    if (Program.Settings.LoggingVerbose) logger.LogError(TLE.MBFin, "Loading temporary track from queue: {trackTitle}", dbTrack?.Title ?? "<NULL>");

                    if (isTempTrack)
                        tempTracks.IsPlaying = true;
                }
            }

            return (isTempTrack, dbTrack);
        }

        public static async Task<bool> DeletePastStatusMessage(Guild guild, DiscordChannel outputChannel) {
            try {
                if (guild.LastMessageStatusId != null && outputChannel != null) {
                    ulong lastMessageStatusId = guild.LastMessageStatusId.Value;
                    var oldMessage = await outputChannel.GetMessageAsync(lastMessageStatusId, true);
                    if (oldMessage != null) {
                        guild.LastMessageStatusId = null;

                        await oldMessage.DeleteAsync();
                        return true;
                    }
                }
            } catch { }

            return false;
        }

        private Task LavalinkNode_Disconnected(LavalinkNodeConnection sender, DSharpPlus.Lavalink.EventArgs.NodeDisconnectedEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_Disconnected, IsCleanClose={IsCleanClose}", args.IsCleanClose);
            return Task.CompletedTask;
        }

        private async Task LavalinkNode_TrackStuck(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStuckEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_TrackStuck");

            // Check if we have a channel for the guild
            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            }

            await client.SendMessageAsync(outputChannel, "Umm... This is embarrassing my music player seems to jammed. *WHAM* *wimpered whiring* " +
                "That'll do it... Eh.... Um..... yeah....... Ehhh, That made it works... Alright well this is your problem now!");
        }

        private async Task LavalinkNode_TrackException(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackExceptionEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_TrackException");

            // Check if we have a channel for the guild
            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            }

            await client.SendMessageAsync(outputChannel, "LavalinkNode_TrackException");
        }

        private async Task LavalinkNode_PlayerUpdated(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.PlayerUpdateEventArgs args) {
            //logger.LogInformation(TLE.Misc, "LavalinkNode_PlayerUpdated, {Position}.", args.Position.ToDynamicTimestamp());

            var guildState = GuildStates.ContainsKey(conn.Guild.Id) ? GuildStates[conn.Guild.Id] : null;

            if (args.Player.CurrentState.CurrentTrack != null && guildState != null && guildState.MusicEmbed != null) {
                var outputChannel = await GetMusicTextChannelFor(conn.Guild);
                if (outputChannel == null) goto Finish;

                // Check if the track exits early (maybe seeking?)
                if (args.Position.TotalSeconds > args.Player.CurrentState.CurrentTrack.Length.TotalSeconds) {
                    goto Finish;
                }

                var progressBar = GenerateProgressBar(args.Position.TotalSeconds, args.Player.CurrentState.CurrentTrack.Length.TotalSeconds, 20);
                var remainingText = GetTrackRemaining(args.Position, args.Player.CurrentState.CurrentTrack.Length);

                var embed = guildState.MusicEmbed.Embed;
                var fieldIdx = guildState.MusicEmbed.FieldIndex;

                var progressText = $"```{remainingText.currentTime} {progressBar} {remainingText.timeLeft}```";
                
                if (guildState.TrackChapters == null || guildState.TrackChapters?.Count <= 1) {
                    embed.Fields[fieldIdx].Value = progressText;
                } else {
                    (YoutubeChaptersParser.IVideoChapter? currentTrack, TimeSpan startTime, TimeSpan? endTime)? result 
                        = guildState?.TrackChapters?.GetNearestByItemTimeSpanWithTimespanRegion<YoutubeChaptersParser.IVideoChapter?>(args.Position);

                    if (result == null || result.Value.currentTrack == null) {
                        embed.Fields[fieldIdx].Value = progressText;
                    } else {
                        var posTotalSeconds = args.Position.TotalSeconds;
                        //var trackTotalSeconds = args.Player.CurrentState.CurrentTrack.Length.TotalSeconds;
                        var currentTrack = result.Value.currentTrack;
                        var startTime    = result.Value.startTime;
                        var endTime      = result.Value.endTime;

                        if (endTime == null) {
                            var trackLength = args.Player.CurrentState.CurrentTrack.Length;

                            progressBar   = GenerateProgressBar(posTotalSeconds - startTime.TotalSeconds, trackLength.TotalSeconds - startTime.TotalSeconds, 20);
                            remainingText = GetTrackRemaining(args.Position - startTime, trackLength - startTime);
                        } else {
                            progressBar   = GenerateProgressBar(posTotalSeconds - startTime.TotalSeconds, endTime.Value.TotalSeconds - startTime.TotalSeconds, 20);
                            remainingText = GetTrackRemaining(args.Position - startTime, endTime.Value - startTime);
                        }

                        embed.Fields[fieldIdx].Name = "Current Track";
                        embed.Fields[fieldIdx].Value = progressText + $"```{currentTrack.Title}\n{remainingText.currentTime} {progressBar} {remainingText.timeLeft}```";

                        // Update the thumbnail
                        if (currentTrack.Thumbnails.Any()) 
                            embed.Thumbnail.Url = currentTrack.Thumbnails.OrderByDescending(x => x.Height).First().Url;
                    }
                }

                var message = guildState.MusicEmbed.Message;
                try {
                    await message.ModifyAsync((DiscordEmbed)guildState.MusicEmbed.Embed);
                } catch (DSharpPlus.Exceptions.NotFoundException ex) {
                    guildState.MusicEmbed = null;
                }
            }

        Finish:
            await BotTimeoutHandler.Instance.UpdateMusicLastActivity(conn);
        }

        private async Task LavalinkNode_PlaybackStarted(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStartEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_PlaybackStarted");

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);
            var guildState = GuildStates[guild.Id];
            guildState.TrackChapters = null; // Reset playlist tracks to null

            if (guildState == null) {
                guildState = new GuildState(guild.Id);
                GuildStates.Add(guild.Id, guildState);
            }

            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            } else {
                await DeletePastStatusMessage(guild, outputChannel);
            }

            GuildQueueItem? dbTrack = null;

            var currentTrackIdx = guild.CurrentTrack;

            var requestedBy = "<#ERROR>";
            var currentTrackQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id && x.Position == currentTrackIdx);
            if (currentTrackQuery.Any()) {
                dbTrack = await currentTrackQuery.FirstAsync();
                requestedBy = (dbTrack?.RequestedBy == null) ? "<#NULL>" : dbTrack?.RequestedBy.DisplayName;
            }

            string? thumbnail = null;
            bool isYoutubeUrl = (args.Track.Uri.Host == "youtube.com" || args.Track.Uri.Host == "www.youtube.com");
            if (isYoutubeUrl) {
                var uriQuery = HttpUtility.ParseQueryString(args.Track.Uri.Query);
                var videoId = uriQuery["v"];

                thumbnail = $"https://img.youtube.com/vi/{videoId}/0.jpg";
            }

            DiscordEmbedBuilder embed = new DiscordEmbedBuilder() {
                Url = $"https://youtube.com/watch?v={args.Track.Identifier}",
                Color = DiscordColor.SpringGreen,
                Title = args.Track.Title,
            };

            if (thumbnail != null)
                embed.WithThumbnail(thumbnail);

            embed.WithAuthor(args.Track.Author);
            //embed.AddField("Player Panel", "[Manage bot through web panel (not added)](https://callumcarmicheal.com/#)", false);

            if (dbTrack == null)
                embed.AddField("Position", "<TRX Nil>", true);
            else embed.AddField("Position", dbTrack.Position.ToString(), true);

            embed.AddField("Duration", args.Track.Length.ToString(@"hh\:mm\:ss"), true);
            embed.AddField("Requested by", requestedBy, true);

            if (guildState.ShuffleEnabled)
                embed.AddField("Shuffle", "Enabled", true);

            if (guildState.RepeatEnabled)
                embed.AddField("Repeat", $"Repeated `{guildState.TimesRepeated}` times.", true);

            if (dbTrack != null)
                embed.AddField("Date", Formatter.Timestamp(dbTrack.CreatedAt, TimestampFormat.LongDateTime), true);

            var embedIndex = embed.Fields.Count;
            var progressBar = GenerateProgressBar(0, args.Track.Length.TotalSeconds, 20);
            var (currentTime, timeLeft) = GetTrackRemaining(TimeSpan.FromSeconds(0), args.Track.Length);
            embed.AddField("Progress", $"```{progressBar} {timeLeft}```");

            // 
            embed.WithFooter($"gb:callums-basement@{Program.VERSION_Full}");

            var message = await client.SendMessageAsync(outputChannel, embed: embed);
            guild.LastMessageStatusId = message.Id;
            guild.IsPlaying = true;
            await db.SaveChangesAsync();

            guildState.MusicEmbed = new MusicEmbedState() {
                Message = message,
                Embed = embed,
                FieldIndex = embedIndex
            };

            if (isYoutubeUrl && Program.Settings.YoutubeIntegration.Enabled && args.Track.Length.TotalMinutes >= 5) 
                _ = Task.Run(() => ParsePlaylist(guild.Id, currentTrackIdx, args.Track.Identifier));

            logger.LogInformation(TLE.Misc, "LavalinkNode_PlaybackStarted <-- Done processing");
        }

        private async Task ParsePlaylist(ulong guildId, ulong currentTrack, string videoUrl) {
            var (success, chapters) = await YoutubeChaptersParser.ParseChapters(videoUrl);
            if (success == false) return;

            var db = new TavernContext();
            var guild = await db.GetGuild(guildId);

            // Check if we are no longer on the same song, then dont update the chapter title.
            if (guild?.CurrentTrack != currentTrack) 
                return;

            GuildStates[guildId].TrackChapters = chapters;
        }


        private async Task LavalinkNode_PlaybackFinished(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackFinishEventArgs args) {
            logger.LogInformation(TLE.MBFin, "-------------PlaybackFinished : {reason}", args.Reason.ToString());
            if (args.Reason == DSharpPlus.Lavalink.EventArgs.TrackEndReason.Replaced) {
                logger.LogInformation(TLE.MBFin, "Finished current track because the music was replaced.");
                return;
            }

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(conn.Guild);
            var guildState = GuildStates.ContainsKey(dbGuild.Id) ? GuildStates[dbGuild.Id] : null;
            var shuffleEnabled = guildState != null && guildState.ShuffleEnabled;
            var repeatEnabled  = guildState != null && guildState.RepeatEnabled;

            if (repeatEnabled) shuffleEnabled = false; // Disable shuffle if on repeat mode!

            // Set IsPlaying to false.
            dbGuild.IsPlaying = false;
            await db.SaveChangesAsync();

            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBFin, "Failed to get music channel for lavalink connection.");
            } else {
                await DeletePastStatusMessage(dbGuild, outputChannel);
            }

            bool isTempTrack = false;
            GuildQueueItem? dbTrack = null;

            // Check if we have any temporary tracks and remove empty playlist if needed
            //(isTempTrack, dbTrack) = this.GetNextTemporaryTrackForGuild(guild);

            if (isTempTrack && Program.Settings.LoggingVerbose) {
                logger.LogInformation(TLE.MBFin, "Playing temporary track: {Title}.", dbTrack?.Title ?? "<NULL>");
            }

            // Check if we are on the repeat mode.
            if (repeatEnabled) {
                // Get the current track
                dbTrack = await getNextTrackForGuild(conn.Guild, dbGuild.CurrentTrack);

                if (guildState != null)
                    guildState.TimesRepeated++;
            } 
            else {
                // Get the next available track
                dbTrack = await getNextTrackForGuild(conn.Guild);

                if (guildState != null)
                    guildState.TimesRepeated = 0;
            }

            // Get the track
            LavalinkTrack? track = null;

            if (dbTrack != null)
                track = await conn.Node.Rest.DecodeTrackAsync(dbTrack.TrackString);

            // Get the next track (attempt it 10 times)
            int attempts = 0;
            int MAX_ATTEMPTS = 10;

            ulong nextTrackNumber = dbGuild.NextTrack;

            while (track == null && attempts++ < MAX_ATTEMPTS) {
                logger.LogInformation(TLE.MBFin, "Error, Failed to parse next track `{Title}` at position `{Position}`.", dbTrack?.Title, dbTrack?.Position);
                if (outputChannel != null && nextTrackNumber <= dbGuild.TrackCount)
                    await outputChannel.SendMessageAsync($"Error (1), Failed to parse next track `{dbTrack?.Title}` at position `{nextTrackNumber}`.");

                // Get the next track
                nextTrackNumber++;

                // If we have reached the max count disconnect
                if (!shuffleEnabled && nextTrackNumber > dbGuild.TrackCount) {
                    if (Program.Settings.LoggingVerbose)
                        logger.LogInformation(TLE.MBFin, "Reached end of playlist count {attempts} attempts, {trackCount} tracks.", attempts, dbGuild.TrackCount);

                    if (outputChannel != null) {
                        string messageText = "Finished queue.";

                        if (dbGuild.LeaveAfterQueue) {
                            // Remove temporary playlist
                            if (TemporaryTracks.ContainsKey(dbGuild.Id))
                                TemporaryTracks.Remove(dbGuild.Id);

                            messageText = "Disconnected after finished queue.";
                            await conn.DisconnectAsync();
                        }

                        await outputChannel.SendMessageAsync(messageText);
                        await db.SaveChangesAsync();
                    }

                    return;
                }

                dbTrack = await getNextTrackForGuild(conn.Guild, nextTrackNumber);
                track = dbTrack == null ? null : await conn.Node.Rest.DecodeTrackAsync(dbTrack?.TrackString);
            }

            // If we cannot still resolve a track leave the channel (if setting provides)
            if (dbTrack == null || track == null) {
                logger.LogInformation(TLE.MBLava, "Fatal error, Failed to parse {MaxAttempts} track(s) in a row at position {Position}. dbTrack == null: {dbTrackIsNull}, track == null: {trackIsNull}"
                    , MAX_ATTEMPTS, dbTrack?.Position, dbTrack == null ? "True" : "False", track == null ? "True" : "False");

                if (outputChannel != null)
                    await outputChannel.SendMessageAsync($"Error (2), Failed to parse next track at position `{nextTrackNumber}`.\n"
                        + $"Please manually set next queue index above `{nextTrackNumber}` with jump or queue a new song!");

                if (dbGuild.LeaveAfterQueue) {
                    // Remove temporary playlist
                    if (TemporaryTracks.ContainsKey(dbGuild.Id))
                        TemporaryTracks.Remove(dbGuild.Id);

                    // Disconnecting
                    await conn.DisconnectAsync();
                    if (outputChannel != null)
                        await outputChannel.SendMessageAsync("Disconnected after finished queue.");
                }

                return;
            }

            // Fix the missing track string
            track.TrackString = dbTrack?.TrackString;

            // Update guild in database
            dbGuild.IsPlaying = true;

            if (isTempTrack == false && dbTrack != null) {
                dbGuild.CurrentTrack = dbTrack.Position;
                dbGuild.NextTrack = dbTrack.Position + 1;
            }

            await db.SaveChangesAsync();

            if (track == null || track.TrackString == null) {
                logger.LogError(TLE.MBLava, "Fatal error, track is null or track string is null!");
                return;
            }

            // Play the next track.
            await Task.Delay(500);
            await conn.PlayAsync(track);
            logger.LogInformation(TLE.Misc, "-------------PlaybackFinished ### Finished processing");
        }

        internal async Task<bool> AttemptReconnectionWithCommandContext(CommandContext ctx) {
            var message = await ctx.RespondAsync("The Lavalink connection is not established, attempting reconnection please wait...");
            var lavalink = client.GetLavalink();

            if (lavalink.ConnectedNodes.Count > 0)
                goto connectionSuccessfulMessage;

            // Reconnect
            logger.LogInformation(TLE.MBSetup, "Lavalink disconnected, attempting reconnection.");

            int retries = 0;
            LavalinkNodeConnection? lavalinkNode = null;

            while (retries < 3 && (lavalinkNode == null || lavalinkNode.IsConnected == false)) {
                lavalinkNode = await Lavalink.ConnectAsync(_lavalinkConfiguration);

                if (lavalinkNode.IsConnected) {
                    lavalinkNode.PlayerUpdated += LavalinkNode_PlayerUpdated;
                    lavalinkNode.PlaybackStarted += LavalinkNode_PlaybackStarted;
                    lavalinkNode.PlaybackFinished += LavalinkNode_PlaybackFinished;
                    lavalinkNode.TrackException += LavalinkNode_TrackException;
                    lavalinkNode.TrackStuck += LavalinkNode_TrackStuck;
                    logger.LogInformation(TLE.MBSetup, "Lavalink connection successful");

                    goto connectionSuccessfulMessage;
                } else {
                    logger.LogInformation(TLE.Startup, "Failed to reconnect to lavalink ({retries}/3)", retries);

                    _ = Task.Delay(500).ContinueWith(async _ => await message.ModifyAsync("Failed to connect to lavalink server after 3 retries."));
                    _ = Task.Delay(120 * 1000).ContinueWith(async _ => { try { await message.DeleteAsync(); } catch { } });
                }

                await message.ModifyAsync($"Lavalink disconnected, attempting reconnection ({retries + 1}/3)");
                retries++;
            }

            return false;

        connectionSuccessfulMessage:
            // Update text in 500 ms and delete message in 60 seconds.
            _ = Task.Delay(500).ContinueWith(async _ => await message.ModifyAsync("Successfully reconnected to lavalink"));
            _ = Task.Delay(60 * 1000).ContinueWith(async _ => { try { await message.DeleteAsync(); } catch { } });
            return true;
        }

        private string GenerateProgressBar(double value, double maxValue, int size) {
            var percentage = value / maxValue; // Calculate the percentage of the bar
            var progress = (int)Math.Round((size * percentage)); // Calculate the number of square caracters to fill the progress side.
            var emptyProgress = size - progress; // Calculate the number of dash caracters to fill the empty progress side.

            var progressText = new string('─', progress - 1 < 0 ? 0 : progress - 1); // Repeat is creating a string with progress * caracters in it
            var emptyProgressText = new string('─', emptyProgress); // Repeat is creating a string with empty progress * caracters in it
            //var percentageText = (int)Math.Round((double)percentage * 100) + '%'; // Displaying the percentage of the bar

            // Creating the bar
            string bar = (progress >= 1)
                ? $@"[{progressText + "⊙" + emptyProgressText}]"
                : $@"[{progressText + emptyProgressText}]";

            return bar;
        }

        private (string currentTime, string timeLeft) GetTrackRemaining(TimeSpan Current, TimeSpan Length) =>
            (currentTime: Current.ToDynamicTimestamp(alwaysShowMinutes: true), 
                timeLeft: "-" + (Length - Current).ToDynamicTimestamp(alwaysShowMinutes: false));
    }
}
