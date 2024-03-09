using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus;

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

namespace CCTavern {
    public class BotTimeoutHandler {

        public TimeSpan TsTimeout = TimeSpan.FromMinutes(5);

        private static Dictionary<ulong, DateTime> lastActivityTracker = new();
        private CancellationTokenSource cancelToken;
        private ILogger<BotTimeoutHandler> logger;
        private DiscordClient discordClient;

        internal async Task UpdateMusicLastActivity(object conn) {
            throw new NotImplementedException(); // TODO: Figure this out.

            /*
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);

            if (lastActivityTracker.ContainsKey(guild.Id))
                lastActivityTracker[guild.Id] = DateTime.Now;
            else {
                lock (lastActivityTracker) {
                    if (lastActivityTracker.ContainsKey(guild.Id))
                        lastActivityTracker[guild.Id] = DateTime.Now;
                    else lastActivityTracker.Add(guild.Id, DateTime.Now);
                }
            }

            logger.LogDebug(TLE.MBTimeout, "Guild {guildId}, Updated timeout!", guild.Id);
            */
        }

        public BotTimeoutHandler(DiscordClient discordClient, ILogger<BotTimeoutHandler> logger) {
            cancelToken = new CancellationTokenSource();

            this.logger = logger;
            logger.LogInformation(TLE.MBTimeout, "Timeout handler starting.");

            this.discordClient = discordClient;

            _ = PeriodicAsync(handleBotTimeouts, TimeSpan.FromMinutes(1), cancelToken.Token);
        }

        ~BotTimeoutHandler() {
            cancelToken.Dispose();
        }

        private async Task handleBotTimeouts() {
            throw new NotImplementedException(); // TODO: Implement
        }
        

        /*
        private async Task handleBotTimeouts() {
            throw new NotImplementedException(); // TODO: Figure this out.

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
                    var guild = await discordClient.GetGuildAsync(guildId);
                    var con = discordClient.GetLavalink().GetGuildConnection(guild);

                    // If we don't have a connection remove it from the dictionary
                    if (con == null) {
                        removals.Add(timeout.Key);
                        continue;
                    }

                    var outputChannel = await MusicBotHelper.GetMusicTextChannelFor(discordClient, guild);
                    var voiceChannel  = con.Channel;

                    // Check if we still want the timeout, to avoid a timetime condition
                    timeout = lastActivityTracker.ElementAt(index);

                    if (dt <= DateTime.Now) {
                        swTimeout.Stop();

                        logger.LogInformation(TLE.MBTimeout, "Guild {guildId} still timedout, disconnecting. ({timeout})", guildId, swTimeout.Elapsed.ToString("mm\\:ss\\.ff"));

                        if (outputChannel != null) {
                            await discordClient.SendMessageAsync(outputChannel, "Left the voice channel <#" + voiceChannel.Id + "> due to inactivity.");
                            await MusicBotHelper.DeletePastStatusMessage(dbGuild, outputChannel);
                        }

                        var lava = discordClient.GetLavalink();
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
                        MusicBotHelper.AnnounceLeave(voiceChannel);

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
        //*/

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
