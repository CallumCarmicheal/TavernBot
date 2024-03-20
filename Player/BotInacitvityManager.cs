using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus;
using DSharpPlus.Lavalink;

using Lavalink4NET;
using Lavalink4NET.Protocol.Models.RoutePlanners;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Org.BouncyCastle.Crypto.Prng;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Player {
    internal class BotInactivityManager {

        /// <summary>
        /// The amount of time of inactivity for the bot to disconnect after.
        /// </summary>
        public TimeSpan TsTimeout = TimeSpan.FromMinutes(5);

        private static Dictionary<ulong, DateTime> lastActivityTracker = new();
        private CancellationTokenSource cancelToken;
        private ILogger<BotInactivityManager> logger;
        private readonly DiscordClient client;
        private readonly MusicBotHelper mbHelper;
        private readonly IAudioService audioService;

        public BotInactivityManager(DiscordClient client, MusicBotHelper mbHelper, IAudioService audioService, ILogger<BotInactivityManager> logger) {
            this.logger = logger;
            this.client = client;
            this.mbHelper = mbHelper;
            this.audioService = audioService;

            cancelToken = new CancellationTokenSource();
            logger.LogInformation(TLE.MBTimeout, "Timeout handler starting.");

            _ = PeriodicAsync(handleBotTimeouts, TimeSpan.FromMinutes(1), cancelToken.Token);
        } 

        internal void UpdateMusicLastActivity(ulong guildId) {
            if (lastActivityTracker.ContainsKey(guildId))
                lastActivityTracker[guildId] = DateTime.Now;
            else {
                lock (lastActivityTracker) {
                    if (lastActivityTracker.ContainsKey(guildId))
                         lastActivityTracker[guildId] = DateTime.Now;
                    else lastActivityTracker.Add(guildId, DateTime.Now);
                }
            }

            logger.LogDebug(TLE.MBTimeout, "Guild {guildId}, Updated timeout!", guildId);
        }

        private async Task handleBotTimeouts() {
            logger.LogDebug(TLE.MBTimeout, "Timeout clearup starting...");
            var sw = new Stopwatch();
            sw.Start();

            var db = new TavernContext();

            List<ulong> removals = new List<ulong>();

            for (int index = 0; index < lastActivityTracker.Count; index++) {
                KeyValuePair<ulong, DateTime> timeout = lastActivityTracker.ElementAt(index);
                var guildId = timeout.Key;

                var dbGuild = await db.Guilds.Where(x => x.Id == guildId).FirstOrDefaultAsync();
                if (dbGuild == null) continue;

                var dt = timeout.Value.Add(TsTimeout);

                if (dt <= DateTime.Now) {
                    Stopwatch swTimeout = new Stopwatch();
                    swTimeout.Start();

                    logger.LogInformation(TLE.MBTimeout, "Guild {guildId} has timedout, getting information...", guildId);

                    // Handle the timeout
                    var guild = await client.GetGuildAsync(guildId);
                    var playerQuery = await mbHelper.GetPlayerAsync(guildId, voiceChannelId: null, connectToVoiceChannel: false);

                    // If we don't have a connection remove it from the dictionary
                    if (playerQuery.isPlayerConnected == false || playerQuery.playerResult.Player == null) {
                        removals.Add(timeout.Key);
                        continue;
                    }

                    var outputChannel = await mbHelper.GetMusicTextChannelFor(guild);
                    var voiceChannelId = playerQuery.playerResult.Player.VoiceChannelId;

                    // Check if we still want the timeout, to avoid a racetime condition
                    timeout = lastActivityTracker.ElementAt(index);

                    if (dt <= DateTime.Now) {
                        swTimeout.Stop();

                        logger.LogInformation(TLE.MBTimeout, "Guild {guildId} still timedout, disconnecting. ({timeout})", guildId, swTimeout.Elapsed.ToString("mm\\:ss\\.ff"));

                        if (outputChannel != null) {
                            await client.SendMessageAsync(outputChannel, "Left the voice channel <#" + voiceChannelId + "> due to inactivity.");
                            await mbHelper.DeletePastStatusMessage(dbGuild, outputChannel);
                        }

                        var lava = client.GetLavalink();
                        if (!lava.ConnectedNodes.Any()) {
                            removals.Add(timeout.Key);
                            continue;
                        }

                        var node = lava.ConnectedNodes.Values.First();
                        var conn = node?.GetGuildConnection(guild);
                        if (conn == null) {
                            removals.Add(timeout.Key);
                            continue;
                        }

                        await conn.DisconnectAsync();
                        mbHelper.AnnounceLeave(guildId);

                        removals.Add(timeout.Key);
                    } else {
                        swTimeout.Stop();
                        logger.LogInformation(TLE.MBTimeout, "Guild {guildId} is no longer ready for timeout. ({timeout})", guildId, swTimeout.Elapsed.ToString());
                    }
                }
            }

            foreach (var id in removals)
                lastActivityTracker.Remove(id);

            sw.Stop();
            logger.LogDebug(TLE.MBTimeout, "Timeout clearup finished, clearup took ({sw})...", sw.Elapsed.ToString());
        }


        public static async Task PeriodicAsync(Func<Task> action, TimeSpan interval,
                CancellationToken cancellationToken = default) {
            using var timer = new PeriodicTimer(interval);
            while (true) {
#if (DEBUG)
                await action();
#else
                try {
                    await action();
                } catch { }
#endif
                await timer.WaitForNextTickAsync(cancellationToken);
            }
        }
    }
}
