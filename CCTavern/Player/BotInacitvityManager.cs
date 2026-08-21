using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus;

using Lavalink4NET;
using Lavalink4NET.Players;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Player {
    public delegate void DUpdateGuildPlayerState(ulong guildId, PlayerState state);

    public class BotInactivityManager {
        public event DUpdateGuildPlayerState OnGuildStateUpdated;

        public void GuildStateChanged(ulong guildId, PlayerState state) => OnGuildStateUpdated?.Invoke(guildId, state);
    }

    public class GuildActivityState {
        public ulong GuildId { get; set; }
        public PlayerState State { get; set; }
        public DateTime LastActivity { get; set; }
        public DateTime? PausedDate { get; set; }
    }

    public class BotInactivityImplementation {
        private readonly ILogger<BotInactivityManager> logger;
        private readonly DiscordClient client;
        private readonly MusicBotHelper mbHelper;
        private readonly IAudioService audioService;
        private readonly ITavernSettings settings;
        private readonly BotInactivityManager inactivityManager;
        private readonly IDbContextFactory<TavernContext> databaseFactory;

        internal ConcurrentDictionary<ulong, GuildActivityState> lastActivityTracker = new();
        private CancellationTokenSource cancelToken;

        private static DateTime? LastTimerTick;

        public TimeSpan TsTimeoutInactivity; /// The amount of time of inactivity for the bot to disconnect after.
        public TimeSpan TsTimeoutPaused; /// The amount of time of paused before the bot disconnects.

        public BotInactivityImplementation(DiscordClient client, MusicBotHelper mbHelper, BotInactivityManager inactivityManager
                , IAudioService audioService, ITavernSettings settings, ILogger<BotInactivityManager> logger
                , IDbContextFactory<TavernContext> databaseFactory) {
            this.logger = logger;
            this.client = client;
            this.mbHelper = mbHelper;
            this.audioService = audioService;
            this.settings = settings;
            this.inactivityManager = inactivityManager;
            this.databaseFactory = databaseFactory;

            cancelToken = new CancellationTokenSource();
            TsTimeoutInactivity = TimeSpan.FromMinutes(settings.InactivityTimerTimeoutInMinutes);
            TsTimeoutPaused     = TimeSpan.FromMinutes(settings.InactivityTimerTimeoutPausedInMinutes);

            logger.LogInformation(TLE.MBTimeout, "Timeout handler starting.");
            _ = PeriodicAsync(handleBotTimeouts, TimeSpan.FromMinutes(1), cancelToken.Token);

            this.inactivityManager.OnGuildStateUpdated += InactivityManager_OnGuildStateUpdated;
        }

        private void InactivityManager_OnGuildStateUpdated(ulong guildId, PlayerState state) {
            // Handle the bot being destroyed
            if (state == PlayerState.Destroyed) {
                lastActivityTracker.TryRemove(guildId, out _);
                return;
            }

            DateTime now = DateTime.Now;

            // Get the activity state
            GuildActivityState activity = lastActivityTracker.GetOrAdd(
                guildId,
                id => new GuildActivityState { GuildId = id });

            if (state == PlayerState.Paused && activity.State != PlayerState.Paused)
                activity.PausedDate = now;
            else if (state != PlayerState.Paused)
                activity.PausedDate = null;

            activity.LastActivity = now;
            activity.State = state;

            logger.LogDebug(
               TLE.MBTimeout,
               "Guild {guildId}, Updated timeout - Player Status {status}!",
               guildId,
               state);
        }

        private async Task handleBotTimeouts() {
            // logger.LogDebug(TLE.MBTimeout, "Timeout clearup starting...");
            var sw = new Stopwatch();
            sw.Start();

            await using var db = await databaseFactory.CreateDbContextAsync();
            List<ulong> removals = new List<ulong>();

            for (int index = 0; index < lastActivityTracker.Count; index++) {
                var timeout = lastActivityTracker.ElementAt(index);
                var guildId = timeout.Key;

                var dbGuild = await db.Guilds.Where(x => x.Id == guildId).FirstOrDefaultAsync();
                if (dbGuild == null) continue;

                DateTime dt
                    = timeout.Value.State == PlayerState.Paused
                    ? timeout.Value.LastActivity.Add(TsTimeoutPaused)
                    : timeout.Value.LastActivity.Add(TsTimeoutInactivity);

                if (dt <= DateTime.Now) {
                    Stopwatch swTimeout = new Stopwatch();
                    swTimeout.Start();

                    logger.LogInformation(TLE.MBTimeout, "Guild {guildId} has timedout, getting information...", guildId);

                    // Handle the timeout
                    var guild = await client.GetGuildAsync(guildId);
                    var playerQueryTavern = await mbHelper.GetPlayerAsync(guildId, voiceChannelId: null, connectToVoiceChannel: false);

                    // If we don't have a connection remove it from the dictionary
                    if (playerQueryTavern.isPlayerConnected == false || playerQueryTavern.playerResult.Player == null) {
                        removals.Add(timeout.Key);
                        continue;
                    }

                    var outputChannel = await mbHelper.GetMusicTextChannelFor(guild);
                    var voiceChannelId = playerQueryTavern.playerResult.Player.VoiceChannelId;

                    // Check if we still want the timeout, to avoid a racetime condition
                    timeout = lastActivityTracker.ElementAt(index);

                    if (dt <= DateTime.Now) {
                        swTimeout.Stop();

                        logger.LogInformation(TLE.MBTimeout, "Guild {guildId} still timedout, disconnecting. ({timeout})", guildId, swTimeout.Elapsed.ToString("mm\\:ss\\.ff"));

                        if (outputChannel != null) {
                            var leaveMessage = "Left the voice channel <#" + voiceChannelId + "> due to inactivity";
                            if (timeout.Value.State == PlayerState.Paused)
                                leaveMessage += " (Paused for too long).";
                            else leaveMessage += ".";

                            await client.SendMessageAsync(outputChannel, leaveMessage);
                            await mbHelper.DeletePastStatusMessage(dbGuild, outputChannel);
                        }

                        // Disconnect the bot
                        await playerQueryTavern.playerResult.Player.DisconnectAsync().ConfigureAwait(false);
                        mbHelper.AnnounceLeave(guildId);
                        db.AddGuildEvent(
                            guildId,
                            GuildEventKind.PlayerDisconnected,
                            GuildEventSource.System,
                            new PlayerConnectionPayload(
                                voiceChannelId,
                                timeout.Value.State == PlayerState.Paused
                                    ? "paused timeout"
                                    : "inactivity timeout"));
                        await db.SaveChangesAsync();

                        // Dispose the player
                        await playerQueryTavern.playerResult.Player.DisposeAsync().ConfigureAwait(false);

                        removals.Add(timeout.Key);
                    } else {
                        swTimeout.Stop();
                        logger.LogInformation(TLE.MBTimeout, "Guild {guildId} is no longer ready for timeout. ({timeout})", guildId, swTimeout.Elapsed.ToString());
                    }
                }
            }

            foreach (var id in removals)
                lastActivityTracker.TryRemove(id, out var _);

            sw.Stop();
            // logger.LogDebug(TLE.MBTimeout, "Timeout clearup finished, clearup took ({sw})...", sw.Elapsed.ToString());
        }

        public bool IsCancellationRequested() => this.cancelToken.IsCancellationRequested;

        public DateTime? GetLastTimerTick()   => LastTimerTick;

        public static async Task PeriodicAsync(Func<Task> action, TimeSpan interval,
                CancellationToken cancellationToken = default) {
            using var timer = new PeriodicTimer(interval);
            while (true) {
                LastTimerTick = DateTime.Now;

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