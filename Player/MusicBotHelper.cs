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
    public class MusicBotHelper
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

        public static async Task<GuildQueueItem?> CreateGuildQueueItem(
            TavernContext db, Guild dbGuild,
            ITrackQueueItem trackQueueItem, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, ulong trackPosition
        ) {
            var track = trackQueueItem.Track;

            if (track == null)
                return null;

            string trackTitle = track.Title;

            if (trackQueueItem is TavernPlayerQueueItem tpqi) {
                trackTitle = tpqi.TrackTitle;
            }

            var requestedUser = await db.GetOrCreateCachedUser(dbGuild, requestedBy);
            var qi = new GuildQueueItem() {
                GuildId = channel.Guild.Id,
                Length = track.Duration,
                Position = trackPosition,
                RequestedById = requestedUser.Id,
                Title = trackTitle,
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

        public async Task<ulong?> EnqueueTrack(ITrackQueueItem trackQueueItem, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, bool updateNextTrack) {
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(channel.Guild);

            dbGuild.TrackCount = dbGuild.TrackCount + 1;
            var trackPosition = dbGuild.TrackCount;
            await db.SaveChangesAsync();

            logger.LogInformation(TLE.Misc, $"Queue Music into {channel.Guild.Name}.{channel.Name} [{trackPosition}] from {requestedBy.Username}: {trackQueueItem.Track?.Title}, {trackQueueItem.Track?.Duration.ToString(@"hh\:mm\:ss")}");

            var qi = await CreateGuildQueueItem(db, dbGuild, trackQueueItem, channel, requestedBy, playlist, trackPosition);

            if (qi == null) {
                logger.LogInformation(TLE.Misc, $"Failed to EnqueueTrack, ITrackQueueItem.Track is null!");
                return null;
            }

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
            var playerOptions = Options.Create(new TavernPlayerOptions() {
                VoiceChannelId = voiceChannelId
            });

            return await GetPlayerAsyncT<TavernPlayer, TavernPlayerOptions>(CreatePlayerAsync, playerOptions, guildId, voiceChannelId, connectToVoiceChannel);
        }

        public async ValueTask<(PlayerResult<TPlayer> playerResult, bool isPlayerConnected)> 
            GetPlayerAsyncT<TPlayer, TOptions>(
                PlayerFactory<TPlayer, TOptions> playerFactory, IOptions<TOptions> playerOptions,
                ulong guildId, ulong? voiceChannelId = null, bool connectToVoiceChannel = true
            )
            where TPlayer  : class, ILavalinkPlayer
            where TOptions : LavalinkPlayerOptions 
        {
            var channelBehavior = connectToVoiceChannel
                ? PlayerChannelBehavior.Join
                : PlayerChannelBehavior.None;

            var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: channelBehavior);

            var result = await audioService.Players
                .RetrieveAsync<TPlayer, TOptions>(
                    guildId: guildId,
                    memberVoiceChannel: voiceChannelId,
                    playerFactory: playerFactory,
                    options: playerOptions,
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
                inactivityManager.GuildStateChanged(guildId, PlayerState.Playing);
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

}
