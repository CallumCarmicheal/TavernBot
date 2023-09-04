using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus.Lavalink;

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
    internal class BotTimeoutHandler {

        public TimeSpan TsTimeout = TimeSpan.FromMinutes(5);

        private static BotTimeoutHandler _instance;
        public static BotTimeoutHandler Instance { 
            get {
                if (_instance == null)
                    _instance = new BotTimeoutHandler();
                return _instance;
            } 
        }

        private static Dictionary<ulong, DateTime> lastActivityTracker = new();
        private CancellationTokenSource cancelToken;
        private ILogger<BotTimeoutHandler> logger;

        internal async Task UpdateMusicLastActivity(LavalinkGuildConnection conn) {
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(conn.Guild);

            if (lastActivityTracker.ContainsKey(guild.Id))
                 lastActivityTracker[guild.Id] = DateTime.Now;
            else lastActivityTracker.Add(guild.Id, DateTime.Now);

            logger.LogInformation(TLE.MBTimeout, "Guild {guildId}, Updated timeout!", guild.Id);
        }

        public BotTimeoutHandler() {
            cancelToken = new CancellationTokenSource();

            logger = Program.LoggerFactory.CreateLogger<BotTimeoutHandler>();
            logger.LogInformation(TLE.MBTimeout, "Timeout handler starting.");

            _ = PeriodicAsync(handleBotTimeouts, TimeSpan.FromMinutes(1), cancelToken.Token);
        }

        ~BotTimeoutHandler() {
            cancelToken.Dispose();
        }

        private async Task handleBotTimeouts() {
            logger.LogDebug(TLE.MBTimeout, "Timeout clearup starting...");
            var sw = new Stopwatch();
            sw.Start();

            var db = new TavernContext();   
            var client = Program.Client;

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
                    var con = client.GetLavalink().GetGuildConnection(guild);

                    // If we don't have a connection remove it from the dictionary
                    if (con == null) {
                        removals.Add(timeout.Key);
                        continue;
                    }

                    var outputChannel = await MusicBot.GetMusicTextChannelFor(guild);
                    var voiceChannel  = con.Channel;

                    // Check if we still want the timeout, to avoid a timetime condition
                    timeout = lastActivityTracker.ElementAt(index);

                    if (dt <= DateTime.Now) {
                        swTimeout.Stop();

                        logger.LogInformation(TLE.MBTimeout, "Guild {guildId} still timedout, disconnecting. ({timeout})", guildId, swTimeout.Elapsed.ToString("mm\\:ss\\.ff"));

                        if (outputChannel != null) {
                            await client.SendMessageAsync(outputChannel, "Left the voice channel <#" + voiceChannel.Id + "> due to inactivity.");
                            await MusicBot.DeletePastStatusMessage(dbGuild, outputChannel);
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
                        MusicBot.AnnounceLeave(voiceChannel);

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
