﻿using System;
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

namespace CCTavern {
    public class MusicBot {
        private readonly DiscordClient client;
        private readonly ILogger logger;

        public LavalinkExtension Lavalink { get; private set; }
        public LavalinkNodeConnection LavalinkNode { get; private set; }

        private Dictionary<ulong, DelayedMethodCaller>  musicTimeouts = new Dictionary<ulong, DelayedMethodCaller>();
        public Dictionary<ulong, TemporaryQueue>        TemporaryTracks = new Dictionary<ulong, TemporaryQueue>();

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


        private Dictionary<ulong, ulong> guildMusicChannelTempCached { get; set; } = new Dictionary<ulong, ulong>();

        public async Task<DiscordChannel?> GetMusicTextChannelFor(DiscordGuild guild) {
            var db = new TavernContext();
            Guild dbGuild = await db.GetOrCreateDiscordGuild(guild);

            // Check if we have that in the database
            ulong? discordChannelId = null;
            
            if (dbGuild.MusicChannelId == null) {
                if (guildMusicChannelTempCached.ContainsKey(guild.Id)) 
                    discordChannelId = guildMusicChannelTempCached[guild.Id];
            } else discordChannelId = dbGuild.MusicChannelId;

            if (discordChannelId == null) return null;
            return await client.GetChannelAsync(discordChannelId.Value);
        }

        public void announceJoin(DiscordChannel channel) {
            if (channel == null) return;

            if (!guildMusicChannelTempCached.ContainsKey(channel.Guild.Id))
                guildMusicChannelTempCached[channel.Guild.Id] = channel.Id;
        }

        public void announceLeave(DiscordChannel channel) {
            if (channel == null) return;

            if (guildMusicChannelTempCached.ContainsKey(channel.Guild.Id))
                guildMusicChannelTempCached.Remove(channel.Guild.Id);
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

            if (targetTrackId == null)
                targetTrackId = guild.NextTrack;

            var query = db.GuildQueueItems.Where(
                x => x.GuildId   == guild.Id 
                  && x.Position  >= targetTrackId 
                  && x.IsDeleted == false);

            if (query.Any() == false)
                return null;

            return await query.FirstAsync();
        }

        public async Task<bool> deletePastStatusMessage(Guild guild, DiscordChannel outputChannel) {
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

        internal async void HandleTimeoutFor(LavalinkGuildConnection conn) {
#if (ARCHIVAL_MODE == false)
            const int timeout = (1000 * 60) * 5; // Wait 5 minutes.
            DelayedMethodCaller delayed;

            if (musicTimeouts.ContainsKey(conn.Guild.Id) == false) {
                delayed = new DelayedMethodCaller(timeout);
                musicTimeouts.Add(conn.Guild.Id, delayed);
            } else {
                delayed = musicTimeouts[conn.Guild.Id];
            }

            if (conn.CurrentState.CurrentTrack == null) {
                var guild = conn.Guild;
                var voiceChannel = conn.Channel;

                var db = new TavernContext();
                var dbGuild = await db.GetOrCreateDiscordGuild(guild);
                dbGuild.IsPlaying = false;
                await db.SaveChangesAsync();
                
                delayed.CallMethod(async () => {
                    var db = new TavernContext();
                    Guild dbGuild = await db.GetOrCreateDiscordGuild(guild);

                    var outputChannel = await GetMusicTextChannelFor(guild);
                    if (outputChannel == null) {
                        logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
                    } else {
                        await client.SendMessageAsync(outputChannel, "Left the voice channel <#" + voiceChannel.Id + "> due to inactivity.");
                        await deletePastStatusMessage(dbGuild, outputChannel);
                    }

                    var lava = client.GetLavalink();
                    if (!lava.ConnectedNodes.Any()) 
                        return;

                    var node = lava.ConnectedNodes.Values.First();
                    var conn = node?.GetGuildConnection(guild);
                    if (conn == null) 
                        return;

                    await conn.DisconnectAsync();
                    announceLeave(voiceChannel);
                });
            } else {
                // We are playing music so stop the cancellation token.
                delayed.Stop();
            }
#endif
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
            HandleTimeoutFor(conn);
        }

        private async Task LavalinkNode_PlaybackStarted(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStartEventArgs args) {
            logger.LogInformation(TLE.Misc, "LavalinkNode_PlaybackStarted");

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);
            
            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            } else {
                await deletePastStatusMessage(guild, outputChannel);
            }

            bool isTempTrack = false;
            GuildQueueItem? dbTrack = null;

            // Check if we have any temporary tracks and remove empty playlist if needed
            if (TemporaryTracks.ContainsKey(guild.Id)) {
                var tempTracks = TemporaryTracks[guild.Id];
                tempTracks.SongItems.FirstOrDefault()?.FinishedPlaying(tempTracks);

                // Remove empty queue
                if (tempTracks.SongItems.Any() == false || tempTracks.SongItems.FirstOrDefault() == null) {
                    TemporaryTracks.Remove(guild.Id);
                }

                // Get the track
                else {
                    dbTrack = tempTracks.SongItems.First()?.GetQueueItem();
                    isTempTrack = dbTrack != null;

                    if (isTempTrack)
                        tempTracks.IsPlaying = true;
                }
            }

            var requestedBy = "<ERROR>";
            if (dbTrack == null) {
                var currentTrackQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id && x.Position == guild.CurrentTrack);
                if (currentTrackQuery.Any()) {
                    dbTrack = await currentTrackQuery.FirstAsync();
                }
            }

            if (dbTrack?.RequestedById != null) {
                var query = db.CachedUsers.Where(x => x.UserId == dbTrack.RequestedById && x.GuildId == guild.Id);
                CachedUser? requestedByUser = null;

                if (await query.AnyAsync())
                    requestedByUser = await query.FirstAsync();

                requestedBy = (requestedByUser == null) ? "<#DELETED>" : requestedByUser.DisplayName;
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
                 embed.AddField("Position", "<ERR>", true);
            else embed.AddField("Position", dbTrack.Position.ToString(), true);
            embed.AddField("Duration", args.Track.Length.ToString(@"hh\:mm\:ss"), true);
            embed.AddField("Requested by", requestedBy, true);
            embed.WithFooter("gb:callums-basement@" + Program.VERSION_Full);

            var message = await client.SendMessageAsync(outputChannel, embed: embed);
            guild.LastMessageStatusId = message.Id;
            guild.IsPlaying = true;
            await db.SaveChangesAsync();
            logger.LogInformation(TLE.Misc, "LavalinkNode_PlaybackStarted <-- Done processing");
        }

        private async Task LavalinkNode_PlaybackFinished(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackFinishEventArgs args) {
            logger.LogInformation(TLE.Misc, "-------------PlaybackFinished : " + args.Reason.ToString());
            if (args.Reason == DSharpPlus.Lavalink.EventArgs.TrackEndReason.Replaced) 
                return;

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);

            // Set IsPlaying to false.
            guild.IsPlaying = false;
            await db.SaveChangesAsync();

            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TLE.MBLava, "Failed to get music channel for lavalink connection.");
            } else {
                await deletePastStatusMessage(guild, outputChannel);
            }

            bool isTempTrack = false;
            GuildQueueItem? dbTrack = null;

            // Check if we have any temporary tracks and remove empty playlist if needed
            if (TemporaryTracks.ContainsKey(guild.Id)) {
                var tempTracks = TemporaryTracks[guild.Id];
                tempTracks.SongItems.FirstOrDefault()?.FinishedPlaying(tempTracks);

                // Remove empty queue
                if (tempTracks.SongItems.Any() == false || tempTracks.SongItems.FirstOrDefault() == null) {
                    TemporaryTracks.Remove(guild.Id);
                } 
                
                // Get the track
                else {
                    dbTrack = tempTracks.SongItems.First()?.GetQueueItem();
                    isTempTrack = dbTrack != null;

                    if (isTempTrack) 
                        tempTracks.IsPlaying = true;
                }
            }

            // Get the next track (attempt it 3 times)
            int attempts = 0;
            int MAX_ATTEMPTS = 3;
            var nextTrackNumber = guild.NextTrack;

            while (dbTrack == null && attempts++ < MAX_ATTEMPTS) 
                dbTrack = await getNextTrackForGuild(conn.Guild, nextTrackNumber++);

            if (dbTrack == null) {
                if (outputChannel != null) {
                    string messageText = "Finished queue.";

                    if (guild.LeaveAfterQueue) {
                        // Remove temporary playlist
                        if (TemporaryTracks.ContainsKey(guild.Id))
                            TemporaryTracks.Remove(guild.Id);

                        messageText = "Disconnected after finished queue.";
                        await conn.DisconnectAsync();
                    }

                    var message = await outputChannel.SendMessageAsync(messageText);
                    guild.LastMessageStatusId = message.Id;
                    await db.SaveChangesAsync();
                }

                return;
            }

            // Get the next song
            var track = await conn.Node.Rest.DecodeTrackAsync(dbTrack.TrackString);
            if (track == null) {
                logger.LogError(TLE.MBLava, $"Error, Failed to parse next track `{dbTrack.Title}` at position `{dbTrack.Position}`.");
                if (outputChannel != null) 
                    await outputChannel.SendMessageAsync($"Error, Failed to parse next track `{dbTrack.Title}` at position `{dbTrack.Position}`.");
                
                return;
            }

            // Fix the missing track string
            track.TrackString = dbTrack.TrackString;

            // Update guild in database
            guild.IsPlaying = true;

            if (isTempTrack == false) {
                guild.CurrentTrack = dbTrack.Position;
                guild.NextTrack = dbTrack.Position + 1;
            }

            await db.SaveChangesAsync();

            // Play the next track.
            await Task.Delay(500);
            await conn.PlayAsync(track);
            logger.LogInformation(TLE.Misc, "-------------PlaybackFinished ### Finished processing");
        }
    }
}
