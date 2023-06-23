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

namespace CCTavern {
    public class MusicBot {
        private readonly DiscordClient client;
        private readonly ILogger logger;

        public LavalinkExtension Lavalink { get; private set; }
        public LavalinkNodeConnection LavalinkNode { get; private set; }

        public MusicBot(DiscordClient client) {
            this.client = client;

            this.logger = Program.LoggerFactory.CreateLogger("MusicBot");
        }

        public async Task SetupLavalink() {
            logger.LogInformation(TavernLogEvents.MBSetup, "Setting up lavalink");

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

            // LavalinkNode.PlayerUpdated += LavalinkNode_PlayerUpdated;
            LavalinkNode.PlaybackStarted += LavalinkNode_PlaybackStarted;
            LavalinkNode.PlaybackFinished += LavalinkNode_PlaybackFinished;
            LavalinkNode.TrackException += LavalinkNode_TrackException;
            LavalinkNode.TrackStuck += LavalinkNode_TrackStuck;
            
            logger.LogInformation(TavernLogEvents.MBSetup, "Lavalink successful");
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

        public async Task<ulong> enqueueMusicTrack(LavalinkTrack track, DiscordChannel channel, DiscordMember requestedBy, GuildQueuePlaylist? playlist, bool updateNextTrack) {
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(channel.Guild);

            dbGuild.TrackCount = dbGuild.TrackCount + 1;
            var trackPosition = dbGuild.TrackCount;
            await db.SaveChangesAsync();

            logger.LogInformation(TavernLogEvents.Misc, $"Queue Music into {channel.Guild.Name}.{channel.Name} [{trackPosition}] from {requestedBy.Username}: {track.Title}, {track.Length.ToString(@"hh\:mm\:ss")}");
            
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

            if (updateNextTrack) {
                dbGuild.NextTrack = trackPosition + 1;
                logger.LogInformation(TavernLogEvents.Misc, $"Setting next track to current position.");
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
            if (guild.LastMessageStatusId != null && outputChannel != null) {
                ulong lastMessageStatusId = guild.LastMessageStatusId.Value;
                try {
                    var oldMessage = await outputChannel.GetMessageAsync(lastMessageStatusId);
                    if (oldMessage != null) {
                        guild.LastMessageStatusId = null;

                        await oldMessage.DeleteAsync();
                        return true;
                    }
                } catch { }
            }
            return false;
        }

        private async Task LavalinkNode_TrackStuck(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStuckEventArgs args) {
            logger.LogInformation(TavernLogEvents.Misc, "LavalinkNode_TrackStuck");

            // Check if we have a channel for the guild
            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TavernLogEvents.MBLava, "Failed to get music channel for lavalink connection.");
            }

            await client.SendMessageAsync(outputChannel, "Umm... This is embarrassing my music player seems to jammed. *WHAM* *wimpered whiring* " +
                "That'll do it... Eh.... Um..... yeah....... Ehhh, That made it works... Alright well this is your problem now!");
        }

        private async Task LavalinkNode_TrackException(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackExceptionEventArgs args) {
            logger.LogInformation(TavernLogEvents.Misc, "LavalinkNode_TrackException");

            // Check if we have a channel for the guild
            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TavernLogEvents.MBLava, "Failed to get music channel for lavalink connection.");
            }

            await client.SendMessageAsync(outputChannel, "LavalinkNode_TrackException");
        }

        private void LavalinkNode_PlayerUpdated(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.PlayerUpdateEventArgs args) {
            logger.LogInformation(TavernLogEvents.Misc, "LavalinkNode_PlayerUpdated");

            /*
            // TODO: Cache this statement
            // Check if we have a channel for the guild
            // var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            // if (outputChannel == null) {
            //     logger.LogError(TavernLogEvents.MBLava, "Failed to get music channel for lavalink connection.");
            // }
            //await client.SendMessageAsync(outputChannel, "LavalinkNode_PlayerUpdated");
            */
        }

        private async Task LavalinkNode_PlaybackStarted(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackStartEventArgs args) {
            logger.LogInformation(TavernLogEvents.Misc, "LavalinkNode_PlaybackStarted");

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);
            
            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TavernLogEvents.MBLava, "Failed to get music channel for lavalink connection.");
            } else {
                await deletePastStatusMessage(guild, outputChannel);
                await db.SaveChangesAsync();
            }

            GuildQueueItem? dbTrack = null;
            var requestedBy = "<ERROR>";
            var currentTrackQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id && x.Position == guild.CurrentTrack);
            if (currentTrackQuery.Any()) {
                dbTrack = await currentTrackQuery.FirstAsync();
                requestedBy = (dbTrack.RequestedBy == null) ? "<#DELETED>" : dbTrack.RequestedBy.DisplayName;
            }

            string? thumbnail = null;
            if (args.Track.Uri.Host == "youtube.com" || args.Track.Uri.Host == "www.youtube.com") {
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
            embed.AddField("Player Panel", "[Manage bot through web panel (not added)](https://callumcarmicheal.com/#)", false);
            if (dbTrack == null)
                 embed.AddField("Position", "<ERR>", true);
            else embed.AddField("Position", dbTrack.Position.ToString(), true);
            embed.AddField("Duration", args.Track.Length.ToString(@"hh\:mm\:ss"), true);
            embed.AddField("Requested by", requestedBy, true);
            embed.WithFooter("gb:callums-basement");

            var message = await client.SendMessageAsync(outputChannel, embed: embed);
            guild.LastMessageStatusId = message.Id;
            guild.IsPlaying = true;
            db.SaveChanges();
        }

        private async Task LavalinkNode_PlaybackFinished(LavalinkGuildConnection conn, DSharpPlus.Lavalink.EventArgs.TrackFinishEventArgs args) {
            logger.LogInformation(TavernLogEvents.Misc, "LavalinkNode_PlaybackFinished");

            // Check if we have a channel for the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);

            // Set IsPlaying to false.
            guild.IsPlaying = false;
            await db.SaveChangesAsync();

            var outputChannel = await GetMusicTextChannelFor(conn.Guild);
            if (outputChannel == null) {
                logger.LogError(TavernLogEvents.MBLava, "Failed to get music channel for lavalink connection.");
            } else {
                await deletePastStatusMessage(guild, outputChannel);
            }

            // Get the next track
            var dbTrack = await getNextTrackForGuild(conn.Guild);
            if (dbTrack == null) {
                if (outputChannel != null) {
                    var message = await outputChannel.SendMessageAsync("Finished queue.");
                    guild.LastMessageStatusId = message.Id;
                    await db.SaveChangesAsync();
                }

                return;
            }

            // Get the next song
            var track = await conn.Node.Rest.DecodeTrackAsync(dbTrack.TrackString);

            if (track == null) {
                logger.LogError(TavernLogEvents.MBLava, $"Error, Failed to parse next track `{dbTrack.Title}` at position `{dbTrack.Position}`.");

                if (outputChannel != null) 
                    await outputChannel.SendMessageAsync($"Error, Failed to parse next track `{dbTrack.Title}` at position `{dbTrack.Position}`.");

                return;
            }

            // Fix the missing track string
            track.TrackString = dbTrack.TrackString;

            // Update guild in database
            guild.IsPlaying = true;
            guild.CurrentTrack = dbTrack.Position;
            guild.NextTrack = dbTrack.Position + 1;
            await db.SaveChangesAsync();

            // Play the next track.
            await Task.Delay(500);
            await conn.PlayAsync(track);
        }
    }
}
