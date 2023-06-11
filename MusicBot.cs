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

            LavalinkNode.PlaybackStarted += LavalinkNode_PlaybackStarted;
            LavalinkNode.PlaybackFinished += LavalinkNode_PlaybackFinished;
            LavalinkNode.PlayerUpdated += LavalinkNode_PlayerUpdated;
            LavalinkNode.TrackException += LavalinkNode_TrackException;
            LavalinkNode.TrackStuck += LavalinkNode_TrackStuck;

            logger.LogInformation(TavernLogEvents.MBSetup, "Lavalink successful");
        }
        
        public Dictionary<ulong, ulong> GuildMusicJoinChannel { get; set; } = new Dictionary<ulong, ulong>();


        private async Task LavalinkNode_TrackStuck(LavalinkGuildConnection sender, DSharpPlus.Lavalink.EventArgs.TrackStuckEventArgs args) {
            // Check if we have a channel for the guild

        }

        private async Task LavalinkNode_TrackException(LavalinkGuildConnection sender, DSharpPlus.Lavalink.EventArgs.TrackExceptionEventArgs args) {
            //
        }

        private async Task LavalinkNode_PlayerUpdated(LavalinkGuildConnection sender, DSharpPlus.Lavalink.EventArgs.PlayerUpdateEventArgs args) {
            //
        }

        private async Task LavalinkNode_PlaybackStarted(LavalinkGuildConnection sender, DSharpPlus.Lavalink.EventArgs.TrackStartEventArgs args) {
            //
        }

        private async Task LavalinkNode_PlaybackFinished(LavalinkGuildConnection sender, DSharpPlus.Lavalink.EventArgs.TrackFinishEventArgs args) {
            //
        }


        private async Task<DiscordChannel?> getMusicChannelForGuild(LavalinkGuildConnection conn) {
            if (conn == null) 
                return null;

            if (GuildMusicJoinChannel.ContainsKey(conn.Guild.Id)) {
                // Todo: Set a value for default music channel

                var musicChannel = await client.GetChannelAsync(GuildMusicJoinChannel[conn.Guild.Id]);

                //if (musicChannel == null || musicChannel.)
            }

            return null;
        }
    }
}
