using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus;
using DSharpPlus.Entities;

using Lavalink4NET;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Players;
using Lavalink4NET.Tracks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Player {
    internal class MusicBotHelper
    {
        private readonly IAudioService audioService;
        private readonly ILogger<MusicBotHelper> logger;
        private readonly DiscordClient discordClient;
        private readonly BotInactivityManager inactivityManager;

        internal Dictionary<ulong, GuildState> GuildStates { get; set; } = new Dictionary<ulong, GuildState>();
        internal Dictionary<ulong, TemporaryQueue> TemporaryTracks { get; set; } = new Dictionary<ulong, TemporaryQueue>();

        public MusicBotHelper(DiscordClient client, BotInactivityManager inactivityManager, IAudioService audioService, ILogger<MusicBotHelper> logger)
        {
            this.discordClient = client;
            this.inactivityManager = inactivityManager;
            this.audioService = audioService;
            this.logger = logger;
        }

        public async Task<DiscordChannel?> GetMusicTextChannelFor(DiscordGuild guild)
        {
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(guild);

            // Check if we have that in the database
            ulong? discordChannelId = null;

            if (dbGuild.MusicChannelId == null) {
                if (GuildStates.ContainsKey(guild.Id) && GuildStates[guild.Id].TemporaryMusicChannelId != null)
                    discordChannelId = GuildStates[guild.Id].TemporaryMusicChannelId;
            }
            else discordChannelId = dbGuild.MusicChannelId;

            if (discordChannelId == null) return null;
            return await discordClient.GetChannelAsync(discordChannelId.Value);
        }

        public (bool isTempTrack, GuildQueueItem? dbTrack) GetNextTemporaryTrackForGuild(Guild guild)
        {
            bool isTempTrack = false;
            GuildQueueItem? dbTrack = null;

            if (TemporaryTracks.ContainsKey(guild.Id))
            {
                if (Program.Settings.LoggingVerbose) logger.LogError(TLE.MBFin, "Temporary tarcks discovered");

                var tempTracks = TemporaryTracks[guild.Id];
                tempTracks.SongItems.FirstOrDefault()?.FinishedPlaying(tempTracks);

                // Remove empty queue
                if (tempTracks.SongItems.Any() == false || tempTracks.SongItems.FirstOrDefault() == null)
                {
                    if (Program.Settings.LoggingVerbose) logger.LogError(TLE.MBFin, "Removing empty temporary track queue.");

                    TemporaryTracks.Remove(guild.Id);
                }

                // Get the track
                else
                {
                    dbTrack = tempTracks.SongItems.First()?.GetQueueItem();
                    isTempTrack = dbTrack != null;

                    if (Program.Settings.LoggingVerbose) logger.LogError(TLE.MBFin, "Loading temporary track from queue: {trackTitle}", dbTrack?.Title ?? "<NULL>");

                    if (isTempTrack)
                        tempTracks.IsPlaying = true;
                }
            }

            return (isTempTrack, dbTrack);
        }


        public void AnnounceJoin(ulong guildId, ulong channelId)
        {
            if (!GuildStates.ContainsKey(guildId))
                GuildStates[guildId] = new GuildState(channelId);

            if (GuildStates[guildId].TemporaryMusicChannelId == null)
                GuildStates[guildId].TemporaryMusicChannelId = channelId;
        }

        public void AnnounceLeave(ulong guildId)
        {
            if (GuildStates.ContainsKey(guildId))
                GuildStates.Remove(guildId);
        }

        public async Task<bool> DeletePastStatusMessage(Guild guild, DiscordChannel outputChannel)
        {
            try
            {
                if (guild.LastMessageStatusId != null && outputChannel != null)
                {
                    ulong lastMessageStatusId = guild.LastMessageStatusId.Value;
                    var oldMessage = await outputChannel.GetMessageAsync(lastMessageStatusId, true);
                    if (oldMessage != null)
                    {
                        guild.LastMessageStatusId = null;

                        await oldMessage.DeleteAsync();
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        //
        // Summary:
        //     Asynchronously retrieves the next track for a specific Discord guild.
        //
        // Parameters:
        //   discordGuild:
        //     The Discord guild for which to retrieve the next track.
        //
        //   targetTrackId:
        //     (Optional) The position index of the track to retrieve
        //
        // Returns:
        //     The next available queue item or null if there is none to be found.
        //
        public async Task<GuildQueueItem?> getNextTrackForGuild(DiscordGuild discordGuild, ulong? targetTrackId = null) {
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(discordGuild);

            if (GuildStates.ContainsKey(discordGuild.Id)) {
                var guildState = GuildStates[discordGuild.Id];

                // Check if we are shuffling and there is no target track specified.
                //    and the SetNext flag is not set, if its been set the next track is specified.
                if (guildState != null && targetTrackId == null && guildState.ShuffleEnabled) {
                    // If we specifically set the next track then skip this 
                    if (guildState.SetNextFlag) {
                        // Clear the SetNextFlag
                        guildState.SetNextFlag = false;
                    } else {
                        var guildQueueQuery = db.GuildQueueItems
                            .Where(x => x.GuildId == guild.Id && x.IsDeleted == false)
                            .OrderByDescending(x => x.Position);

                        if (await guildQueueQuery.CountAsync() >= 4) {
                            var rnd = new Random();

                            var largestTrackNumber = await guildQueueQuery.Select(x => x.Position).FirstOrDefaultAsync();
                            targetTrackId = Convert.ToUInt64(rnd.ULongNext(0, largestTrackNumber));
                        } else {
                            if (guildState != null)
                                guildState.ShuffleEnabled = false;
                        }
                    }
                }
            }

            // Set the next target track if not specified
            targetTrackId ??= guild.NextTrack;

            // Get the track from the database
            var query = db.GuildQueueItems
                .Include(x => x.RequestedBy)
                .Where(
                    x => x.GuildId == guild.Id
                      && x.Position >= targetTrackId // Get target track or the next available track
                      && x.IsDeleted == false);

            // Return the track or null.
            if (query.Any() == false)
                return null;

            return await query.FirstAsync();
        }

        public string GenerateProgressBar(double value, double maxValue, int size)
        {
            var percentage = value / maxValue; // Calculate the percentage of the bar
            var progress = (int)Math.Round(size * percentage); // Calculate the number of square caracters to fill the progress side.
            var emptyProgress = size - progress; // Calculate the number of dash caracters to fill the empty progress side.

            var progressText = new string('─', progress - 1 < 0 ? 0 : progress - 1); // Repeat is creating a string with progress * caracters in it
            var emptyProgressText = new string('─', emptyProgress); // Repeat is creating a string with empty progress * caracters in it
            //var percentageText = (int)Math.Round((double)percentage * 100) + '%'; // Displaying the percentage of the bar

            // Creating the bar
            string bar = progress >= 1
                ? $@"[{progressText + "⊙" + emptyProgressText}]"
                : $@"[{progressText + emptyProgressText}]";

            return bar;
        }

        public (string currentTime, string timeLeft) GetTrackRemaining(TimeSpan Current, TimeSpan Length) =>
            (currentTime: Current.ToDynamicTimestamp(alwaysShowMinutes: true),
                timeLeft: "-" + (Length - Current).ToDynamicTimestamp(alwaysShowMinutes: false));

        public async Task ParseYoutubeChaptersPlaylist(ulong guildId, ulong currentTrack, string videoUrl, CancellationToken cancellationToken = default) {
            var (success, chapters) = await YoutubeChaptersParser.ParseChapters(videoUrl, cancellationToken).ConfigureAwait(false);
            if (success == false) return;

            var db = new TavernContext();
            var guild = await db.GetGuild(guildId);

            // Check if we are no longer on the same song, then dont update the chapter title.
            if (guild?.CurrentTrack != currentTrack)
                return;

            GuildStates[guildId].TrackChapters = chapters;
        }

        internal GuildState GetOrCreateGuildState(ulong guildId) {
            if (this.GuildStates.ContainsKey(guildId))
                return GuildStates[guildId];

            GuildStates.Add(guildId, new GuildState(guildId));
            return GuildStates[guildId];
        }


        public static async Task<GuildQueueItem> CreateGuildQueueItem(
                TavernContext db, Guild dbGuild,
                LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, ulong trackPosition
        ) {
            var requestedUser = await db.GetOrCreateCachedUser(dbGuild, requestedBy);
            var qi = new GuildQueueItem() {
                GuildId = channel.Guild.Id,
                Length = track.Duration,
                Position = trackPosition,
                RequestedById = requestedUser.Id,
                Title = track.Title,
                TrackString = track.ToString(),
                PlaylistId = playlist?.Id
            };

            return qi;
        }

        public async Task<ulong> EnqueueTrack(LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, bool updateNextTrack) {
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(channel.Guild);

            dbGuild.TrackCount = dbGuild.TrackCount + 1;
            var trackPosition = dbGuild.TrackCount;
            await db.SaveChangesAsync();

            logger.LogInformation(TLE.Misc, $"Queue Music into {channel.Guild.Name}.{channel.Name} [{trackPosition}] from {requestedBy.Username}: {track.Title}, {track.Duration.ToString(@"hh\:mm\:ss")}");

            var qi = await CreateGuildQueueItem(db, dbGuild, track, channel, requestedBy, playlist, trackPosition);

            if (updateNextTrack) {
                dbGuild.NextTrack = trackPosition + 1;
                logger.LogInformation(TLE.Misc, $"Setting next track to current position.");
            }

            db.GuildQueueItems.Add(qi);
            await db.SaveChangesAsync();

            return trackPosition;
        }

        static ValueTask<TavernPlayer> CreatePlayerAsync(IPlayerProperties<TavernPlayer, TavernPlayerOptions> properties, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(properties);

            var player = new TavernPlayer(properties);
            return ValueTask.FromResult(player);
        }

        public async ValueTask<(PlayerResult<TavernPlayer> playerResult, bool isPlayerConnected)> GetPlayerAsync(ulong guildId, ulong? voiceChannelId = null, bool connectToVoiceChannel = true) {
            var channelBehavior = connectToVoiceChannel
                ? PlayerChannelBehavior.Join
                : PlayerChannelBehavior.None;

            var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: channelBehavior);

            var result = await audioService.Players
                .RetrieveAsync<TavernPlayer, TavernPlayerOptions>(
                    guildId: guildId,
                    memberVoiceChannel: voiceChannelId,
                    playerFactory: CreatePlayerAsync,
                    options: Options.Create(new TavernPlayerOptions() {
                        VoiceChannelId = voiceChannelId
                    }),
                    retrieveOptions: retrieveOptions
                )
                .ConfigureAwait(false);

            if (!result.IsSuccess) {
                return (result, false);
            }

            if (result.Player != null && connectToVoiceChannel) {
                if (voiceChannelId != null)
                    AnnounceJoin(guildId, voiceChannelId.Value);

                // Update inactivity manager, so if !join is used instead of !play it disconnects the bot after inactivity
                inactivityManager.UpdateMusicLastActivity(guildId);
            }

            bool isConnected = result.IsSuccess && result.Player != null //&& result.Player.ConnectionState.IsConnected
                && result.Player.State != PlayerState.Destroyed;

            return (result, isConnected);
        }

        public string GetPlayerErrorMessage(PlayerRetrieveStatus status) {
            var errorMessage = status switch {
                PlayerRetrieveStatus.Success => "Success",
                PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                PlayerRetrieveStatus.VoiceChannelMismatch => "You are not in the same channel as the Music Bot!",
                PlayerRetrieveStatus.UserInSameVoiceChannel => "Same voice channel?",
                PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                PlayerRetrieveStatus.PreconditionFailed => "A unknown error happened: Precondition Failed.",
                _ => "A unknown error happened"
            };

            return errorMessage;
        }

        public string StripYoutubePlaylistFromUrl(string url) {
            Uri originalUri = new Uri(url);
            string queryString = originalUri.Query;

            // Remove parameter from query string
            string newQueryString = string.Join("&",
                queryString.Split('&')
                            .Where(x => 
                                !x.StartsWith("list=", StringComparison.OrdinalIgnoreCase)
                                && !x.StartsWith("index=", StringComparison.OrdinalIgnoreCase)
                                && !x.StartsWith("t=", StringComparison.OrdinalIgnoreCase)
                            )
                            .ToArray());

            // Reconstruct the URL with the modified query string
            UriBuilder uriBuilder = new UriBuilder(originalUri);
            uriBuilder.Query = newQueryString;

            return uriBuilder.ToString();
        }
    }

    /*
    public class MusicBot {
        private readonly DiscordClient client;
        private readonly ILogger logger;
        private readonly BotTimeoutHandler botTimeoutHandler;

        public LavalinkExtension Lavalink { get; private set; }

        private ConnectionEndpoint? _lavalinkEndpoint = null;
        private LavalinkConfiguration? _lavalinkConfiguration = null;

        internal Dictionary<ulong, TemporaryQueue> TemporaryTracks = new Dictionary<ulong, TemporaryQueue>();
        internal static Dictionary<ulong, GuildState> GuildStates { get; set; } = new Dictionary<ulong, GuildState>();

        public MusicBotHelpers(DiscordClient client, BotTimeoutHandler botTimeoutHandler) {
            this.client = client;
            this.logger = Program.LoggerFactory.CreateLogger("MusicBot");
            this.botTimeoutHandler = botTimeoutHandler;
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

        

        private Task LavalinkNode_Disconnected(LavalinkNodeConnection sender, DSharpPlus.Lavalink.EventArgs.NodeDisconnectedEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_Disconnected, IsCleanClose={IsCleanClose}", args.IsCleanClose);
            return Task.CompletedTask;
        }

        private async Task LavalinkNode_TrackStuck(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStuckEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_TrackStuck");

            // Check if we have a channel for the guild
            var outputChannel = await GetMusicTextChannelFor(client, conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            }

            await client.SendMessageAsync(outputChannel, "Umm... This is embarrassing my music player seems to jammed. *WHAM* *wimpered whiring* " +
                "That'll do it... Eh.... Um..... yeah....... Ehhh, That made it works... Alright well this is your problem now!");
        }

        private async Task LavalinkNode_TrackException(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackExceptionEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_TrackException");

            // Check if we have a channel for the guild
            var outputChannel = await GetMusicTextChannelFor(client, conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            }

            await client.SendMessageAsync(outputChannel, "LavalinkNode_TrackException");
        }

        private async Task LavalinkNode_PlayerUpdated(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.PlayerUpdateEventArgs args) {
            //logger.LogInformation(TLE.Misc, "LavalinkNode_PlayerUpdated, {Position}.", args.Position.ToDynamicTimestamp());

            var guildState = GuildStates.ContainsKey(conn.Guild.Id) ? GuildStates[conn.Guild.Id] : null;

            if (args.Player.CurrentState.CurrentTrack != null && guildState != null && guildState.MusicEmbed != null) {
                var outputChannel = await GetMusicTextChannelFor(client, conn.Guild);
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
            await botTimeoutHandler.UpdateMusicLastActivity(conn);
        }

        private async Task LavalinkNode_PlaybackStarted(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStartEventArgs args) {
            
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

            var outputChannel = await GetMusicTextChannelFor(client, conn.Guild);
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


    }

    //*/
}
