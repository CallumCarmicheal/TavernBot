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
using Lavalink4NET;
using MySqlX.XDevAPI.Common;
using System.Reflection;
using Org.BouncyCastle.Asn1;
using System.Security.Policy;

namespace CCTavern.Player
{
    public sealed class TavernPlayer : LavalinkPlayer, IDisposable 
    {
        private readonly ILogger<TavernPlayer> logger;
        private readonly MusicBotHelper mbHelper;
        private readonly DiscordClient discordClient;
        private readonly IAudioService audioService;
        private readonly BotInactivityManager botInactivityManager;

        private Timer _timer;
        private CancellationTokenSource _cancellationTokenSource;

        public TavernPlayer(IPlayerProperties<LavalinkPlayer, LavalinkPlayerOptions> properties)
            : base(properties)
        {
            logger = properties.ServiceProvider!.GetRequiredService<ILogger<TavernPlayer>>();
            mbHelper = properties.ServiceProvider!.GetRequiredService<MusicBotHelper>();
            discordClient = properties.ServiceProvider!.GetRequiredService<DiscordClient>();
            audioService = properties.ServiceProvider!.GetRequiredService<IAudioService>();
            botInactivityManager = properties.ServiceProvider!.GetRequiredService<BotInactivityManager>();

            _cancellationTokenSource = new CancellationTokenSource();
            _timer = new Timer(callback: ProgressBarTimerCallback, state: null, dueTime: Timeout.Infinite, period: Timeout.Infinite);

            logger.LogDebug("TavernPlayer <<<<<<<<< Constructor");
        }

        ~TavernPlayer() {
            logger.LogDebug("TavernPlayer <<<<<<<<< Destructor");
        }

        public void Dispose() {
            StopProgressTimer();
            _cancellationTokenSource.Cancel();
            _timer.Dispose();

            mbHelper.AnnounceLeave(GuildId);

            logger.LogDebug("TavernPlayer <<<<<<<<< Disposed");
        }

        public void StartProgressTimer(TimeSpan? interval = null) {
            if (interval == null)
                interval = TimeSpan.FromSeconds(7); // Every 7 seconds default.

            _timer.Change(TimeSpan.Zero, interval.Value);
        }

        public void StopProgressTimer() {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async void ProgressBarTimerCallback(object? state) {
            // Stop the timer if requested
            if (_cancellationTokenSource.Token.IsCancellationRequested) {
                StopProgressTimer();
                return;
            }

            // Check if the player is not playing.
            if (this.State != PlayerState.Playing) 
                return;

            var guild = await GetGuildAsync();

            // If we dont have a guildState somehow, it means we did not create an embed.
            if (mbHelper.GuildStates[guild.Id] == null) goto Finish;

            GuildState guildState = mbHelper.GuildStates[guild.Id];

            // Dont process this tick if the MusicEmbed is null or if there is no track.
            if (mbHelper.GuildStates[guild.Id] == null || guildState.MusicEmbed == null || CurrentTrack == null)
                goto Finish;

            var outputChannel = await mbHelper.GetMusicTextChannelFor(guild);
            if (outputChannel == null) goto Finish;
            if (this.Position.HasValue == false) goto Finish;

            var embed = guildState.MusicEmbed.Embed;
            if (embed == null) {
                StopProgressTimer();
                return;
            }

            if (updateStateEmbed(guildState, embed) == false) 
                goto Finish;

            var message = guildState?.MusicEmbed.Message;
            if (guildState != null && message != null) {
                try {
                    await message.ModifyAsync((DiscordEmbed)guildState.MusicEmbed.Embed);
                } catch (DSharpPlus.Exceptions.NotFoundException) {
                    guildState.MusicEmbed = null;
                }
            }

        Finish:
            botInactivityManager.GuildStateChanged(guild.Id, State);
        }

        protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem tqi, CancellationToken cancellationToken = default)
        {
            logger.LogInformation(TLE.Misc, "NotifyTrackStartedAsync");
            mbHelper.AnnounceJoin(GuildId, VoiceChannelId);

            var track = tqi.Track;
            if (track == null) {
                // No track is playing, just ignore it.
                return;
            }

            var discordGuild = await GetGuildAsync();
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(discordGuild);
            var guildState = mbHelper.GetOrCreateGuildState(dbGuild.Id);

            if (guildState == null) {
                guildState = new GuildState(dbGuild.Id);
                mbHelper.GuildStates.Add(dbGuild.Id, guildState);
            }

            guildState.TrackChapters = null; // Reset playlist tracks to null

            var outputChannel = await mbHelper.GetMusicTextChannelFor(discordGuild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel.");
            } else {
                await mbHelper.DeletePastStatusMessage(dbGuild, outputChannel);
            }

            GuildQueueItem? dbTrack = null;

            var currentTrackIdx = dbGuild.CurrentTrack;

            var requestedBy = "<#ERROR>";
            var currentTrackQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == dbGuild.Id && x.Position == currentTrackIdx);
            if (currentTrackQuery.Any()) {
                dbTrack = await currentTrackQuery.FirstAsync(cancellationToken);
                requestedBy = (dbTrack?.RequestedBy == null) ? "<#NULL>" : dbTrack?.RequestedBy.DisplayName;
            }

            string track_Author = track.Author;
            string? track_AuthorUrl = null;
            string track_Title = track.Title;

            string? thumbnail = null;

            string? embedUrl  = track.Uri?.ToString();
            bool isYoutubeUrl = (track.Uri?.Host == "youtube.com" || track.Uri?.Host == "www.youtube.com");
            bool isSunoUrl    = (track.Uri?.Host == "suno.com" || (track.Uri?.Host.EndsWith("suno.ai") ?? false));
            if (isYoutubeUrl && track.Uri != null) {
                var uriQuery = HttpUtility.ParseQueryString(track.Uri.Query);
                var videoId = uriQuery["v"];

                embedUrl = $"https://youtube.com/watch?v={track.Identifier}";
                thumbnail = $"https://img.youtube.com/vi/{videoId}/0.jpg";
            }

            else if (isSunoUrl && track.Uri != null) {
                TavernPlayerQueueItem? tavernQueueItem;

                // Check if the ITrackQueueItem is a TavernPlayerQueueItem
                if (tqi is TavernPlayerQueueItem tavernQI) {
                    tavernQueueItem = tavernQI;
                }
                // If we do not have a TavernPlayerQueueItem lets get the metadata again.
                //   this can be when a person jumps the queue like !jump <idx> and the track was placed on the queue directly from the db.
                else {
                    tavernQueueItem = await SunoAIParser.GetSunoTrack(track.Uri.ToString()).ConfigureAwait(false);
                }

                if (tavernQueueItem != null) {
                    track_AuthorUrl = tavernQueueItem.AuthorUrl;
                    track_Author = tavernQueueItem.AuthorDisplayName;
                    track_Title = tavernQueueItem.TrackTitle;
                    embedUrl = tavernQueueItem.TrackUrl;
                    thumbnail = tavernQueueItem.TrackThumbnail;

                    if (!string.IsNullOrEmpty(tavernQueueItem.AuthorSuffix))
                        track_Author += " " + tavernQueueItem.AuthorSuffix;
                }
            }

            DiscordEmbedBuilder embed = new DiscordEmbedBuilder() {
                Url   = embedUrl,
                Color = DiscordColor.SpringGreen,
                Title = track_Title,
            };

            if (thumbnail != null)
                embed.WithThumbnail(thumbnail);

            // Enum.GetValues(typeof(DiscordColor))


            embed.WithAuthor(track_Author, track_AuthorUrl);
            embed.WithColor(DiscordColor.Goldenrod);
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

            if (dbTrack != null) {
                embed.AddField("State", "Playing", true);
                embed.AddField("Date", Formatter.Timestamp(dbTrack.CreatedAt, TimestampFormat.LongDateTime), true);
            } else {
                embed.AddField("State", "Playing");
            }

            var progressBar = mbHelper.GenerateProgressBar(0, track.Duration.TotalSeconds, 20);
            var (currentTime, timeLeft) = mbHelper.GetTrackRemaining(TimeSpan.FromSeconds(0), track.Duration);
            embed.AddField("Progress", $"```{progressBar} {timeLeft}```");

            // 
            embed.WithFooter($"gb:callums-basement@{Program.VERSION_Full}");

            var message = await discordClient.SendMessageAsync(outputChannel, embed: embed);

            dbGuild.LastMessageStatusId = message.Id;
            dbGuild.IsPlaying = true;
            await db.SaveChangesAsync(cancellationToken);

            var embedIndex = embed.Fields.FindIndex(x => x.Name == "Progress");
            var stateIndex = embed.Fields.FindIndex(x => x.Name == "State");
            guildState.MusicEmbed = new MusicEmbedState() {
                Message = message,
                Embed = embed,
                ProgressFieldIdx = embedIndex,
                StateFieldIdx = stateIndex
            };

            if (isYoutubeUrl && Program.Settings.YoutubeIntegration.Enabled && track.Duration.TotalMinutes >= 5)
                _ = Task.Run(() => mbHelper.ParseYoutubeChaptersPlaylist(dbGuild.Id, currentTrackIdx, track.Identifier, _cancellationTokenSource.Token), _cancellationTokenSource.Token);

            StartProgressTimer();
            botInactivityManager.GuildStateChanged(dbGuild.Id, State);
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
            var repeatEnabled  = guildState != null && guildState.RepeatEnabled;

            if (repeatEnabled) shuffleEnabled = false; // Disable shuffle if on repeat mode!

            // Set IsPlaying to false.
            dbGuild.IsPlaying = false;
            await db.SaveChangesAsync();

            // Get the output music chanenl
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
                dbTrack = await mbHelper.getNextTrackForGuild(guild, targetTrackId: dbGuild.CurrentTrack);

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

            // Attempt to get next available track
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
                            await DisconnectAsync().ConfigureAwait(false);
                            mbHelper.AnnounceLeave(dbGuild.Id);
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
                    await DisconnectAsync().ConfigureAwait(false);
                    mbHelper.AnnounceLeave(dbGuild.Id);

                    if (outputChannel != null)
                        await outputChannel.SendMessageAsync("Disconnected after finished queue.");
                }

                return;
            }

            // Update guild in database
            dbGuild.IsPlaying = true;

            if (isTempTrack == false && dbTrack != null) {
                dbGuild.CurrentTrack = dbTrack.Position;
                dbGuild.NextTrack    = dbTrack.Position + 1;
            }

            await db.SaveChangesAsync();

            if (track == null) {
                logger.LogError(TLE.MBLava, "Fatal error, track is null is null!");
                return;
            }

            // Parse the track if its a suno ai track
            bool isSunoUrl = (track.Uri?.Host == "suno.com" || (track.Uri?.Host.EndsWith("suno.ai") ?? false));

            if (isSunoUrl && track != null) {
                TavernPlayerQueueItem? nextTrackQueueItem = await SunoAIParser.GetSunoTrack(track.Uri?.ToString()).ConfigureAwait(false);

                if (nextTrackQueueItem != null) {
                    var trackRef = new TrackReference(track);
                    nextTrackQueueItem.Reference = trackRef;

                    await Task.Delay(500);
                    await PlayAsync(nextTrackQueueItem).ConfigureAwait(false);

                    logger.LogInformation(TLE.Misc, "-------------PlaybackFinished ### Finished processing");
                    return;
                }
            }
            
            // Play the next track.
            await Task.Delay(500);
            await PlayAsync(track!).ConfigureAwait(false);
            
            logger.LogInformation(TLE.Misc, "-------------PlaybackFinished ### Finished processing");
        }

        public override async ValueTask PauseAsync(CancellationToken cancellationToken = default) {
            // Pause the track
            await base.PauseAsync(cancellationToken);

            var discordGuild = await GetGuildAsync();
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(discordGuild);
            var guildState = mbHelper.GetOrCreateGuildState(dbGuild.Id);

            // Pause the update timer thread
            StopProgressTimer();

            // Update state
            botInactivityManager.GuildStateChanged(dbGuild.Id, State);

            // Update the embed
            if (guildState == null || guildState.MusicEmbed == null)
                return;

            var updateMessage = updateStateEmbed(guildState, guildState.MusicEmbed.Embed);
            if (updateMessage) {
                var message = guildState?.MusicEmbed.Message;
                if (guildState != null && message != null) {
                    try {
                        await message.ModifyAsync((DiscordEmbed)guildState.MusicEmbed.Embed);
                    } catch (DSharpPlus.Exceptions.NotFoundException) {
                        guildState.MusicEmbed = null;
                    }
                }
            }
        }

        public override async ValueTask ResumeAsync(CancellationToken cancellationToken = default) {
            await base.ResumeAsync(cancellationToken);

            var discordGuild = await GetGuildAsync();
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(discordGuild);
            var guildState = mbHelper.GetOrCreateGuildState(dbGuild.Id);

            // Pause the update timer thread
            StartProgressTimer();

            // Update state
            botInactivityManager.GuildStateChanged(dbGuild.Id, State);

            // Update the embed
            if (guildState == null || guildState.MusicEmbed == null)
                return;

            var updateMessage = updateStateEmbed(guildState, guildState.MusicEmbed.Embed);
            if (updateMessage) {
                var message = guildState?.MusicEmbed.Message;
                if (guildState != null && message != null) {
                    try {
                        await message.ModifyAsync((DiscordEmbed)guildState.MusicEmbed.Embed);
                    } catch (DSharpPlus.Exceptions.NotFoundException) {
                        guildState.MusicEmbed = null;
                    }
                }
            }
        }


        private bool updateStateEmbed(GuildState guildState, DiscordEmbedBuilder embedBuilder) {
            if (guildState == null || guildState.MusicEmbed == null || this.Position == null || CurrentTrack == null) 
                return false;

            var progressFieldIdx = guildState.MusicEmbed.ProgressFieldIdx;
            var Position         = this.Position.Value.RelativePosition;

            // Check if the track exits early (maybe seeking?) 
            if (Position.TotalSeconds > CurrentTrack.Duration.TotalSeconds)
                return false;

            // Update current progress bar
            var progressBar   = mbHelper.GenerateProgressBar(Position.TotalSeconds, CurrentTrack.Duration.TotalSeconds, 20);
            var remainingText = mbHelper.GetTrackRemaining(Position, CurrentTrack.Duration);
            var progressText  = $"```{remainingText.currentTime} {progressBar} {remainingText.timeLeft}```";

            // Update track chapter
            if (guildState.TrackChapters == null || guildState.TrackChapters?.Count <= 1) {
                embedBuilder.Fields[progressFieldIdx].Value = progressText;
            } else {
                var result = guildState.TrackChapters?.GetNearestByItemTimeSpanWithTimespanRegion(Position);

                if (result == null || result.Value.item == null) {
                    embedBuilder.Fields[progressFieldIdx].Value = progressText;
                } else {
                    var posTotalSeconds = Position.TotalSeconds;
                    var currentTrack = result.Value.item;
                    var startTime = result.Value.startTime;
                    var endTime = result.Value.endTime;

                    if (endTime == null) {
                        var trackLength = CurrentTrack.Duration;

                        progressBar = mbHelper.GenerateProgressBar(posTotalSeconds - startTime.TotalSeconds, trackLength.TotalSeconds - startTime.TotalSeconds, 20);
                        remainingText = mbHelper.GetTrackRemaining(Position - startTime, trackLength - startTime);
                    } else {
                        progressBar = mbHelper.GenerateProgressBar(posTotalSeconds - startTime.TotalSeconds, endTime.Value.TotalSeconds - startTime.TotalSeconds, 20);
                        remainingText = mbHelper.GetTrackRemaining(Position - startTime, endTime.Value - startTime);
                    }

                    embedBuilder.Fields[progressFieldIdx].Name = "Current Track";
                    embedBuilder.Fields[progressFieldIdx].Value = progressText + $"```{currentTrack.Title}\n{remainingText.currentTime} {progressBar} {remainingText.timeLeft}```";

                    // Update the thumbnail
                    if (currentTrack.Thumbnails.Any())
                        embedBuilder.Thumbnail.Url = currentTrack.Thumbnails.OrderByDescending(x => x.Height).First().Url;
                }
            }

            // Update player state
            var playerStateIdx = guildState.MusicEmbed.StateFieldIdx;
            embedBuilder.Fields[playerStateIdx].Value = State.ToString();

            return true;
        }

        #region Guild and Track Functions

        private DiscordGuild _guild;
        private async Task<DiscordGuild> GetGuildAsync() {
            return _guild ??= await discordClient.GetGuildAsync(GuildId);
        }

        #endregion
    }
}
