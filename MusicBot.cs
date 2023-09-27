using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DSharpPlus.Net;
using DSharpPlus.Lavalink;
using DSharpPlus;
using CCTavern;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Microsoft.Extensions.Logging;
using CCTavern.Logger;
using DSharpPlus.Entities;
using CCTavern.Database;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore.Update;
using System.Security.Policy;
using System.Web;
using CCTavern.Utility;
using System.Reflection;
using System.Diagnostics;
using Org.BouncyCastle.Asn1.Cms;
using System.Threading;
using System.Data.Common;

namespace CCTavern {
    public class MusicBot {
        private readonly DiscordClient client;
        private readonly ILogger logger;

        public LavalinkExtension Lavalink { get; private set; }
        public LavalinkNodeConnection LavalinkNode { get; private set; }

        internal Dictionary<ulong, TemporaryQueue> TemporaryTracks = new Dictionary<ulong, TemporaryQueue>();
        internal static Dictionary<ulong, GuildState> GuildStates { get; set; } = new Dictionary<ulong, GuildState>();

        public MusicBot(DiscordClient client) {
            this.client = client;
            this.logger = Program.LoggerFactory.CreateLogger("MusicBot");
        }

        public async Task SetupLavalink() {
            logger.LogInformation(TLE.MBSetup, "Setting up lavalink");

            var lavalinkSettings = Program.Settings.Lavalink;

            var endpoint = new ConnectionEndpoint {
                Hostname = lavalinkSettings.Hostname,
                Port = lavalinkSettings.Port
            };

            var lavalinkConfig = new LavalinkConfiguration {
                Password = lavalinkSettings.Password, 
                RestEndpoint = endpoint,
                SocketEndpoint = endpoint
            };

            Lavalink = client.UseLavalink();
            LavalinkNode = await Lavalink.ConnectAsync(lavalinkConfig);

            LavalinkNode.PlayerUpdated    += LavalinkNode_PlayerUpdated;
            LavalinkNode.PlaybackStarted  += LavalinkNode_PlaybackStarted;
            LavalinkNode.PlaybackFinished += LavalinkNode_PlaybackFinished;
            LavalinkNode.TrackException   += LavalinkNode_TrackException;
            LavalinkNode.TrackStuck       += LavalinkNode_TrackStuck;
            
            logger.LogInformation(TLE.MBSetup, "Lavalink successful");
        }



        public static async Task<DiscordChannel?> GetMusicTextChannelFor(DiscordGuild guild) {
            var db = new TavernContext();
            Guild dbGuild = await db.GetOrCreateDiscordGuild(guild);

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
                LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, ulong trackPosition) {

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
                    var guildQueueQuery = db.GuildQueueItems.Where(x => x.GuildId == guild.Id && x.IsDeleted == false).OrderByDescending(x => x.Position);

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

            var query = db.GuildQueueItems.Where(
                x => x.GuildId   == guild.Id 
                  && x.Position  >= targetTrackId 
                  && x.IsDeleted == false);

            if (query.Any() == false)
                return null;

            return await query.FirstAsync();
        }

        public async Task<(bool isTempTrack, GuildQueueItem? dbTrack)> getNextTemporaryTrackForGuild(Guild guild) {
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
            await BotTimeoutHandler.Instance.UpdateMusicLastActivity(conn);
        }

        private async Task LavalinkNode_PlaybackStarted(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStartEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_PlaybackStarted");

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);
            var guildState = GuildStates[guild.Id];

            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            } else {
                await DeletePastStatusMessage(guild, outputChannel);
            }

            GuildQueueItem? dbTrack = null;

            var requestedBy = "<#ERROR>";
            var currentTrackQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id && x.Position == guild.CurrentTrack);
            if (currentTrackQuery.Any()) {
                dbTrack = await currentTrackQuery.FirstAsync();
                requestedBy = (dbTrack?.RequestedBy == null) ? "<#NULL>" : dbTrack?.RequestedBy.DisplayName;
            }

            string? thumbnail = null;
            if (args.Track.Uri.Host == "youtube.com" || args.Track.Uri.Host == "www.youtube.com") {
                var uriQuery = HttpUtility.ParseQueryString(args.Track.Uri.Query);
                var videoId = uriQuery["v"];

                thumbnail = $"https://img.youtube.com/vi/{videoId}/0.jpg";
            }

            DiscordEmbedBuilder embed = new DiscordEmbedBuilder() {
                Url   = $"https://youtube.com/watch?v={args.Track.Identifier}",
                Color = DiscordColor.SpringGreen,
                Title = args.Track.Title,
            };

            if (thumbnail != null) 
                embed.WithThumbnail(thumbnail);

            embed.WithAuthor(args.Track.Author);
            embed.AddField("Player Panel", "[Manage bot through web panel (not added)](https://callumcarmicheal.com/#)", false);

            if (dbTrack == null)
                 embed.AddField("Position", "<TRX Nil>", true);
            else embed.AddField("Position", dbTrack.Position.ToString(), true);

            embed.AddField("Duration", args.Track.Length.ToString(@"hh\:mm\:ss"), true);
            embed.AddField("Requested by", requestedBy, true);

            if (guildState != null && guildState.ShuffleEnabled)
                embed.AddField("Shuffle", "Enabled", true);

            if (dbTrack != null)
                embed.AddField("Date", Formatter.Timestamp(dbTrack.CreatedAt, TimestampFormat.LongDateTime), true);
            embed.WithFooter("gb:callums-basement@" + Program.VERSION_Full);

            var message = await client.SendMessageAsync(outputChannel, embed: embed);
            guild.LastMessageStatusId = message.Id;
            guild.IsPlaying = true;
            await db.SaveChangesAsync();
            logger.LogInformation(TLE.Misc, "LavalinkNode_PlaybackStarted <-- Done processing");
        }

        private async Task LavalinkNode_PlaybackFinished(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackFinishEventArgs args) {
            logger.LogInformation(TLE.MBFin, "-------------PlaybackFinished : {reason}", args.Reason.ToString());
            if (args.Reason == DSharpPlus.Lavalink.EventArgs.TrackEndReason.Replaced) {
                logger.LogInformation(TLE.MBFin, "Finished current track because the music was replaced.");
                return;
            }

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);
            var guildState = GuildStates[guild.Id];
            var shuffleEnabled = guildState != null && guildState.ShuffleEnabled;

            // Set IsPlaying to false.
            guild.IsPlaying = false;
            await db.SaveChangesAsync();

            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBFin, "Failed to get music channel for lavalink connection.");
            } else {
                await DeletePastStatusMessage(guild, outputChannel);
            }

            bool            isTempTrack = false;
            GuildQueueItem? dbTrack = null;

            // Check if we have any temporary tracks and remove empty playlist if needed
            //(isTempTrack, dbTrack) = await this.getNextTemporaryTrackForGuild(guild);

            if (isTempTrack && Program.Settings.LoggingVerbose) {
                logger.LogInformation(TLE.MBFin, "Playing temporary track: {Title}.", dbTrack?.Title ?? "<NULL>");
            }

            // Get the next available track
            dbTrack = await getNextTrackForGuild(conn.Guild);

            // Get the track
            LavalinkTrack? track = null;
            
            if (dbTrack != null) 
                track = await conn.Node.Rest.DecodeTrackAsync(dbTrack.TrackString);

            // Get the next track (attempt it 10 times)
            int attempts = 0;
            int MAX_ATTEMPTS = 10;

            ulong nextTrackNumber = guild.NextTrack;

            while (track == null && attempts++ < MAX_ATTEMPTS) {
                logger.LogInformation(TLE.MBFin, "Error, Failed to parse next track `{Title}` at position `{Position}`.", dbTrack?.Title, dbTrack?.Position);
                if (outputChannel != null && nextTrackNumber <= guild.TrackCount)
                    await outputChannel.SendMessageAsync($"Error (1), Failed to parse next track `{dbTrack?.Title}` at position `{nextTrackNumber}`.");

                // Get the next track
                nextTrackNumber++;

                // If we have reached the max count disconnect
                if (!shuffleEnabled && nextTrackNumber > guild.TrackCount) {
                    if (Program.Settings.LoggingVerbose) 
                        logger.LogInformation(TLE.MBFin, "Reached end of playlist count {attempts} attempts, {trackCount} tracks.", attempts, guild.TrackCount);

                    if (outputChannel != null) {
                        string messageText = "Finished queue.";

                        if (guild.LeaveAfterQueue) {
                            // Remove temporary playlist
                            if (TemporaryTracks.ContainsKey(guild.Id))
                                TemporaryTracks.Remove(guild.Id);

                            messageText = "Disconnected after finished queue.";
                            await conn.DisconnectAsync();
                        }

                        await outputChannel.SendMessageAsync(messageText);
                        await db.SaveChangesAsync();
                    }

                    return;
                }

                dbTrack = await getNextTrackForGuild(conn.Guild, nextTrackNumber);
                track   = dbTrack == null ? null : await conn.Node.Rest.DecodeTrackAsync(dbTrack?.TrackString);
            }

            // If we cannot still resolve a track leave the channel (if setting provides)
            if (dbTrack == null || track == null) {
                logger.LogInformation(TLE.MBLava, "Fatal error, Failed to parse {MaxAttempts} track(s) in a row at position {Position}. dbTrack == null: {dbTrackIsNull}, track == null: {trackIsNull}"
                    , MAX_ATTEMPTS, dbTrack?.Position, dbTrack == null ? "True" : "False", track == null ? "True" : "False");

                if (outputChannel != null)
                    await outputChannel.SendMessageAsync($"Error (2), Failed to parse next track at position `{nextTrackNumber}`.\n" 
                        + $"Please manually set next queue index above `{nextTrackNumber}` with jump or queue a new song!");

                if (guild.LeaveAfterQueue) {
                    // Remove temporary playlist
                    if (TemporaryTracks.ContainsKey(guild.Id))
                        TemporaryTracks.Remove(guild.Id);

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
            guild.IsPlaying = true;

            if (isTempTrack == false) {
                guild.CurrentTrack = dbTrack.Position;
                guild.NextTrack    = dbTrack.Position + 1;
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
    }
}
