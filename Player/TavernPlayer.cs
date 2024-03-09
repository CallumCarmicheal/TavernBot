using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Lavalink4NET.Events;
using Lavalink4NET.Clients;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Socket;
using Lavalink4NET.Protocol.Payloads.Events;
using System.Threading;
using CCTavern.Database;
using CCTavern.Logger;
using DSharpPlus.Entities;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using MySqlX.XDevAPI;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Lavalink4NET.Tracks;
using Org.BouncyCastle.Asn1.Cms;
using Lavalink4NET.InactivityTracking.Players;
using Lavalink4NET.InactivityTracking.Trackers;

namespace CCTavern.Player
{
    public sealed class TavernPlayer : LavalinkPlayer, IInactivityPlayerListener, IDisposable 
    {
        private readonly ILogger<TavernPlayer> logger;
        private readonly MusicBotHelper mbHelper;
        private readonly DiscordClient discordClient;

        private Timer _timer;
        private CancellationTokenSource _cancellationTokenSource;

        public TavernPlayer(IPlayerProperties<LavalinkPlayer, LavalinkPlayerOptions> properties)
            : base(properties)
        {
            logger = properties.ServiceProvider!.GetRequiredService<ILogger<TavernPlayer>>();
            mbHelper = properties.ServiceProvider!.GetRequiredService<MusicBotHelper>();
            discordClient = properties.ServiceProvider!.GetRequiredService<DiscordClient>();

            _cancellationTokenSource = new CancellationTokenSource();
            _timer = new Timer(ProgressBarTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose() {
            StopProgressTimer();
            _cancellationTokenSource.Cancel();
            _timer.Dispose();

            mbHelper.AnnounceLeave(GuildId);
        }

        public void StartProgressTimer(TimeSpan? interval = null) {
            if (interval == null)
                interval = TimeSpan.FromSeconds(7); // Every 7 seconds default.

            _timer.Change(TimeSpan.Zero, interval.Value);
        }

        public void StopProgressTimer() {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async void ProgressBarTimerCallback(object state) {
            // Stop the timer if requested
            if (_cancellationTokenSource.Token.IsCancellationRequested) {
                StopProgressTimer();
                return;
            }
            
            // Check if the player is not playing.
            if (this.State != PlayerState.Playing) 
                return;

            var track = CurrentTrack;
            var guild = await GetGuildAsync();

            // If we dont have a guildState somehow, it means we did not create an embed.
            if (mbHelper.GuildStates[guild.Id] == null) return;

            GuildState guildState = mbHelper.GuildStates[guild.Id];

            // Dont process this tick if the MusicEmbed is null or if there is no track.
            if (mbHelper.GuildStates[guild.Id] == null || guildState.MusicEmbed == null || CurrentTrack == null)
                return;

            var outputChannel = await mbHelper.GetMusicTextChannelFor(guild);
            if (outputChannel == null) return;
            if (this.Position.HasValue == false) return;

            var Position = this.Position.Value.Position;

            // Check if the track exits early (maybe seeking?) 
            if (Position.TotalSeconds > CurrentTrack.Duration.TotalSeconds)
                return;

            var progressBar   = mbHelper.GenerateProgressBar(Position.TotalSeconds, CurrentTrack.Duration.TotalSeconds, 20);
            var remainingText = mbHelper.GetTrackRemaining(Position, CurrentTrack.Duration);

            var embed    = guildState.MusicEmbed.Embed;
            var fieldIdx = guildState.MusicEmbed.FieldIndex;

            var progressText = $"```{remainingText.currentTime} {progressBar} {remainingText.timeLeft}```";

            if (guildState.TrackChapters == null || guildState.TrackChapters?.Count <= 1) {
                embed.Fields[fieldIdx].Value = progressText;
            } else {
                (YoutubeChaptersParser.IVideoChapter? currentTrack, TimeSpan startTime, TimeSpan? endTime)? result
                    = guildState?.TrackChapters?.GetNearestByItemTimeSpanWithTimespanRegion<YoutubeChaptersParser.IVideoChapter?>(Position);

                if (result == null || result.Value.currentTrack == null) {
                    embed.Fields[fieldIdx].Value = progressText;
                } else {
                    var posTotalSeconds = Position.TotalSeconds;
                    //var trackTotalSeconds = args.Player.CurrentState.CurrentTrack.Length.TotalSeconds;
                    var currentTrack = result.Value.currentTrack;
                    var startTime    = result.Value.startTime;
                    var endTime      = result.Value.endTime;

                    if (endTime == null) {
                        var trackLength = CurrentTrack.Duration;

                        progressBar   = mbHelper.GenerateProgressBar(posTotalSeconds - startTime.TotalSeconds, trackLength.TotalSeconds - startTime.TotalSeconds, 20);
                        remainingText = mbHelper.GetTrackRemaining(Position - startTime, trackLength - startTime);
                    } else {
                        progressBar   = mbHelper.GenerateProgressBar(posTotalSeconds - startTime.TotalSeconds, endTime.Value.TotalSeconds - startTime.TotalSeconds, 20);
                        remainingText = mbHelper.GetTrackRemaining(Position - startTime, endTime.Value - startTime);
                    }

                    embed.Fields[fieldIdx].Name = "Current Track";
                    embed.Fields[fieldIdx].Value = progressText + $"```{currentTrack.Title}\n{remainingText.currentTime} {progressBar} {remainingText.timeLeft}```";

                    // Update the thumbnail
                    if (currentTrack.Thumbnails.Any())
                        embed.Thumbnail.Url = currentTrack.Thumbnails.OrderByDescending(x => x.Height).First().Url;
                }
            }

            var message = guildState?.MusicEmbed.Message;
            if (guildState != null && message != null) {
                try {
                    await message.ModifyAsync((DiscordEmbed)guildState.MusicEmbed.Embed);
                } catch (DSharpPlus.Exceptions.NotFoundException) {
                    guildState.MusicEmbed = null;
                }
            }
        }

        protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem tqi
            , CancellationToken cancellationToken = default)
        {
            logger.LogInformation(TLE.Misc, "NotifyTrackStartedAsync");
            mbHelper.AnnounceJoin(GuildId, VoiceChannelId);

            var track = tqi.Track;
            if (track == null) {
                // TODO: Handle null track?
                return;
            }

            var discordGuild = await GetGuildAsync();
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(discordGuild);
            var guildState = mbHelper.GetOrCreateGuildState(guild.Id);

            if (guildState == null) {
                guildState = new GuildState(guild.Id);
                mbHelper.GuildStates.Add(guild.Id, guildState);
            }

            guildState.TrackChapters = null; // Reset playlist tracks to null

            var outputChannel = await mbHelper.GetMusicTextChannelFor(discordGuild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel.");
            } else {
                await mbHelper.DeletePastStatusMessage(guild, outputChannel);
            }

            GuildQueueItem? dbTrack = null;

            var currentTrackIdx = guild.CurrentTrack;

            var requestedBy = "<#ERROR>";
            var currentTrackQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id && x.Position == currentTrackIdx);
            if (currentTrackQuery.Any()) {
                dbTrack = await currentTrackQuery.FirstAsync(cancellationToken);
                requestedBy = (dbTrack?.RequestedBy == null) ? "<#NULL>" : dbTrack?.RequestedBy.DisplayName;
            }

            string? thumbnail = null;
            

            bool isYoutubeUrl = (track.Uri?.Host == "youtube.com" || track.Uri?.Host == "www.youtube.com");
            if (isYoutubeUrl && track.Uri != null) {
                var uriQuery = HttpUtility.ParseQueryString(track.Uri.Query);
                var videoId = uriQuery["v"];

                thumbnail = $"https://img.youtube.com/vi/{videoId}/0.jpg";
            }

            DiscordEmbedBuilder embed = new DiscordEmbedBuilder() {
                Url = $"https://youtube.com/watch?v={track.Identifier}",
                Color = DiscordColor.SpringGreen,
                Title = track.Title,
            };

            if (thumbnail != null)
                embed.WithThumbnail(thumbnail);

            embed.WithAuthor(track.Author);
            //embed.AddField("Player Panel", "[Manage bot through web panel (not added)](https://callumcarmicheal.com/#)", false);

            if (dbTrack == null)
                 embed.AddField("Position", "<TRX Nil>", true);
            else embed.AddField("Position", dbTrack.Position.ToString(), true);

            embed.AddField("Duration", track.Duration.ToString(@"hh\:mm\:ss"), true);
            embed.AddField("Requested by", requestedBy, true);

            if (guildState.ShuffleEnabled)
                embed.AddField("Shuffle", "Enabled", true);

            if (guildState.RepeatEnabled)
                embed.AddField("Repeat", $"Repeated `{guildState.TimesRepeated}` times.", true);

            if (dbTrack != null)
                embed.AddField("Date", Formatter.Timestamp(dbTrack.CreatedAt, TimestampFormat.LongDateTime), true);

            var embedIndex = embed.Fields.Count;
            var progressBar = mbHelper.GenerateProgressBar(0, track.Duration.TotalSeconds, 20);
            var (currentTime, timeLeft) = mbHelper.GetTrackRemaining(TimeSpan.FromSeconds(0), track.Duration);
            embed.AddField("Progress", $"```{progressBar} {timeLeft}```");

            // 
            embed.WithFooter($"gb:callums-basement@{Program.VERSION_Full}");

            var message = await discordClient.SendMessageAsync(outputChannel, embed: embed);

            guild.LastMessageStatusId = message.Id;
            guild.IsPlaying = true;
            await db.SaveChangesAsync(cancellationToken);

            guildState.MusicEmbed = new MusicEmbedState() {
                Message = message,
                Embed = embed,
                FieldIndex = embedIndex
            };

            if (isYoutubeUrl && Program.Settings.YoutubeIntegration.Enabled && track.Duration.TotalMinutes >= 5)
                _ = Task.Run(() => mbHelper.ParsePlaylist(guild.Id, currentTrackIdx, track.Identifier), CancellationToken.None);

            StartProgressTimer();
            logger.LogInformation(TLE.Misc, "NotifyTrackStartedAsync <-- Done processing");
        }

        protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem trackQueueItem, TrackEndReason endReason
            , CancellationToken cancellationToken = default) 
        {
            logger.LogInformation(TLE.MBFin, "-------------PlaybackFinished : {reason}", endReason.ToString());
            if (endReason == TrackEndReason.Replaced) {
                logger.LogInformation(TLE.MBFin, "Finished current track because the music was replaced.");
                return;
            }

            var guild = await GetGuildAsync();

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(guild);
            var guildState = mbHelper.GuildStates.ContainsKey(dbGuild.Id) ? mbHelper.GuildStates[dbGuild.Id] : null;
            var shuffleEnabled = guildState != null && guildState.ShuffleEnabled;
            var repeatEnabled = guildState != null && guildState.RepeatEnabled;

            if (repeatEnabled) shuffleEnabled = false; // Disable shuffle if on repeat mode!

            // Set IsPlaying to false.
            dbGuild.IsPlaying = false;
            await db.SaveChangesAsync();

            var outputChannel = await mbHelper.GetMusicTextChannelFor(guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBFin, "Failed to get music channel for lavalink connection.");
            } else {
                await mbHelper.DeletePastStatusMessage(dbGuild, outputChannel);
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
                dbTrack = await mbHelper.getNextTrackForGuild(guild, dbGuild.CurrentTrack);

                if (guildState != null)
                    guildState.TimesRepeated++;
            } else {
                // Get the next available track
                dbTrack = await mbHelper.getNextTrackForGuild(guild);

                if (guildState != null)
                    guildState.TimesRepeated = 0;
            }

            // Get the track
            LavalinkTrack? track = null;

            if (dbTrack != null)
                track = LavalinkTrack.Parse(dbTrack.TrackString, provider: null);

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
                            if (mbHelper.TemporaryTracks.ContainsKey(dbGuild.Id))
                                mbHelper.TemporaryTracks.Remove(dbGuild.Id);

                            messageText = "Disconnected after finished queue.";
                            await DisconnectAsync();
                        }

                        await outputChannel.SendMessageAsync(messageText);
                        await db.SaveChangesAsync();
                    }

                    return;
                }

                dbTrack = await mbHelper.getNextTrackForGuild(guild, nextTrackNumber);
                track = dbTrack == null ? null : LavalinkTrack.Parse(dbTrack.TrackString, provider: null);
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
                    if (mbHelper.TemporaryTracks.ContainsKey(dbGuild.Id))
                        mbHelper.TemporaryTracks.Remove(dbGuild.Id);

                    // Disconnecting
                    await DisconnectAsync();
                    if (outputChannel != null)
                        await outputChannel.SendMessageAsync("Disconnected after finished queue.");
                }

                return;
            }

            // Update guild in database
            dbGuild.IsPlaying = true;

            if (isTempTrack == false && dbTrack != null) {
                dbGuild.CurrentTrack = dbTrack.Position;
                dbGuild.NextTrack = dbTrack.Position + 1;
            }

            await db.SaveChangesAsync();

            if (track == null) {
                logger.LogError(TLE.MBLava, "Fatal error, track is null is null!");
                return;
            }

            // Play the next track.
            await Task.Delay(500);
            await PlayAsync(track).ConfigureAwait(false);
            logger.LogInformation(TLE.Misc, "-------------PlaybackFinished ### Finished processing");
        }


        #region Guild and Track Functions

        private DiscordGuild _guild;
        private async Task<DiscordGuild> GetGuildAsync() {
            return _guild ??= await discordClient.GetGuildAsync(GuildId);
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

        public async Task<ulong> enqueueMusicTrack(LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, bool updateNextTrack) {
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

        #endregion

        #region Tracking

        public async ValueTask NotifyPlayerActiveAsync(PlayerTrackingState trackingState, CancellationToken cancellationToken = default) {
            // This method is called when the player was previously inactive and is now active again.
            // For example: All users in the voice channel left and now a user joined the voice channel again.
            cancellationToken.ThrowIfCancellationRequested();

            // return default; // do nothing
        }

        public async ValueTask NotifyPlayerInactiveAsync(PlayerTrackingState trackingState, CancellationToken cancellationToken = default) {
            // This method is called when the player reached the inactivity deadline.
            // For example: All users in the voice channel left and the player was inactive for longer than 30 seconds.
            cancellationToken.ThrowIfCancellationRequested();

            await DisconnectAsync();

            var guild = await GetGuildAsync();
            var outputChannel = await mbHelper.GetMusicTextChannelFor(guild);
            if (outputChannel == null) return;

            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(guild);

            await discordClient.SendMessageAsync(outputChannel, "Left the voice channel <#" + VoiceChannelId + "> due to inactivity.");
            await mbHelper.DeletePastStatusMessage(dbGuild, outputChannel);
        }

        public async ValueTask NotifyPlayerTrackedAsync(PlayerTrackingState trackingState, CancellationToken cancellationToken = default) {
            // This method is called when the player was previously active and is now inactive.
            // For example: A user left the voice channel and now all users left the voice channel.
            cancellationToken.ThrowIfCancellationRequested();

            // return default; // do nothing
        }
        #endregion
    }
}
