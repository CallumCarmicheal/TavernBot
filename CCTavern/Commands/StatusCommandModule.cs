using CCTavern.Player;

using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

using Lavalink4NET;
using Lavalink4NET.Players;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class StatusCommandModule : BaseAudioCommandModule {
        private readonly ILogger<StatusCommandModule> logger;
        private readonly BotInactivityImplementation  botInactivity;

        public StatusCommandModule(MusicBotHelper mbHelper, BotInactivityImplementation botInactivity, IAudioService audioService, ILogger<StatusCommandModule> logger)
                : base(audioService, mbHelper) {
            this.logger = logger;
            this.botInactivity = botInactivity;
        }

        [Command("status-quick"), Aliases("stats-q")]
        [Description("View the status of the inactivity module")]
        [RequireGuild]
        public async Task StatusInactivity(CommandContext ctx) {
            string status = "";

            var countOfPlayers = audioService.Players.Players.Count();
            status += $"Players Active: {countOfPlayers}\n";

            status += $"```\nGuild Timeout with {botInactivity.lastActivityTracker.Keys.Count} guilds.\n";

            var timerTick = botInactivity.GetLastTimerTick();
            if (timerTick == null)
                 status += $"Last Timer Tick:  | *Timer has not processed a tick*\n";
            else status += $"Last Timer Tick:  | {timerTick.Value:dd/MM/yyyy HH:mm:ss}\n";
            status += $"Inactivity timer: | {botInactivity.TsTimeoutInactivity.ToDynamicTimestamp()}\n";
            status += $"Paused timer:     | {botInactivity.TsTimeoutPaused.ToDynamicTimestamp()}\n";

            status += $"\n{"Guild Id",20} {"Activity",12} {"Paused At",12} {"State",12} {"Autodiscon at.", 14}\n";
            status += $"{"".PadRight(20, '-')} {"".PadRight(12, '-')} {"".PadRight(12, '-')} {"".PadRight(12, '-')} {"".PadRight(14, '-')}\n";
            foreach (var row in botInactivity.lastActivityTracker) {
                var guildId = row.Key;
                var playingState = row.Value;

                var lastActivity       = playingState.LastActivity.ToString("HH:mm:ss");
                var pausedDate         = playingState.PausedDate.HasValue ? playingState.PausedDate.Value.ToString("HH:mm:ss") : "n/a";
                var autoDisconnectTime = "n/a";
                
                if (playingState.State is PlayerState.NotPlaying or PlayerState.Paused or PlayerState.Destroyed) {
                    autoDisconnectTime = playingState.LastActivity.Add(botInactivity.TsTimeoutInactivity).ToString("HH:mm:ss");
                }
                
                status += $"{guildId,20} {lastActivity,12} {pausedDate,12} {playingState.State, 12} {autoDisconnectTime, 14}\n";
            }
            status += "```";

            await ctx.RespondAsync(status).ConfigureAwait(false);
        }


        const string Reset = "\u001b[0m";
        const string BoldCyan = "\u001b[1;36m";
        const string Green = "\u001b[32m";
        const string Yellow = "\u001b[33m";
        const string YellowBackground = "\u001b[30;43m";
        const string Red = "\u001b[31m";
        const string Dim = "\u001b[2m";
        const int SummaryLabelWidth = 8;
        const int SummaryStateWidth = 8;
        const int GuildColumnWidth = 20;
        const int StateColumnWidth = 10;
        const int TimeColumnWidth = 8;
        const int DisconnectColumnWidth = 22;

        [Command("status"), Aliases("stats")]
        [Description("A full system status check page")]
        [RequireGuild]
        public async Task StatusFancy(CommandContext ctx) {
            var now = DateTime.Now;
            var playersActive = audioService.Players.Players.Count();
            var trackedGuilds = botInactivity.lastActivityTracker.ToArray();
            var lastTimerTick = botInactivity.GetLastTimerTick();
            TimeSpan? timerAge = lastTimerTick.HasValue ? now - lastTimerTick.Value : null;
            var isDrifting = playersActive != trackedGuilds.Length;
            var discordHealth = GetDiscordHealth(ctx);
            var lavalinkHealth = await GetLavalinkHealthAsync().ConfigureAwait(false);
            var watchdogIsStale = timerAge == null || timerAge.Value >= TimeSpan.FromMinutes(3);
            var hasWarning = isDrifting || discordHealth.IsHealthy == false || lavalinkHealth.IsHealthy == false || watchdogIsStale;

            var status = new StringBuilder();
            status.AppendLine($"{BoldCyan}Music Bot Status{Reset}  {FormatHealth(hasWarning)}");
            status.AppendLine("----------------------------------------");
            status.AppendLine($"{FormatSummaryLabel("Discord")} {FormatHealthColumn(discordHealth.State, discordHealth.Color)} {discordHealth.Detail}");
            status.AppendLine($"{FormatSummaryLabel("Lavalink")} {FormatHealthColumn(lavalinkHealth.State, lavalinkHealth.Color)} {lavalinkHealth.Detail}");
            status.AppendLine($"{FormatSummaryLabel("Audio")} {FormatAudioState(isDrifting)} local {playersActive,3}   tracked {trackedGuilds.Length,3}");
            status.AppendLine($"{FormatSummaryLabel("Watchdog")} {FormatWatchdogState(timerAge)} last tick {FormatLastTimerTick(lastTimerTick),8}   age {FormatTimerAge(timerAge),8}");
            status.AppendLine($"{FormatSummaryLabel("Timeouts")} idle {botInactivity.TsTimeoutInactivity.ToDynamicTimestamp(),8}   paused {botInactivity.TsTimeoutPaused.ToDynamicTimestamp(),8}");
            status.AppendLine();

            if (trackedGuilds.Length == 0) {
                status.AppendLine($"{Dim}No guilds are currently tracked by the inactivity manager.{Reset}");
            } else {
                status.AppendLine($"{"Guild",GuildColumnWidth}  {"State",StateColumnWidth}  {"Last",TimeColumnWidth}  {"Paused",TimeColumnWidth}  {"Disconnect",DisconnectColumnWidth}");
                status.AppendLine($"{"".PadRight(GuildColumnWidth, '-')}  {"".PadRight(StateColumnWidth, '-')}  {"".PadRight(TimeColumnWidth, '-')}  {"".PadRight(TimeColumnWidth, '-')}  {"".PadRight(DisconnectColumnWidth, '-')}");

                foreach (var row in trackedGuilds
                    .OrderBy(x => x.Value.State == PlayerState.Playing)
                    .ThenBy(x => GetDisconnectAt(x.Value) ?? DateTime.MaxValue)
                    .ThenBy(x => x.Value.LastActivity)
                    .Take(12)) {

                    var guildId = row.Key.ToString();
                    var guildState = row.Value;
                    var lastActivity = guildState.LastActivity.ToString("HH:mm:ss");
                    var pausedAt = guildState.PausedDate.HasValue ? guildState.PausedDate.Value.ToString("HH:mm:ss") : "-";

                    status.AppendLine($"{guildId,GuildColumnWidth}  {FormatState(guildState.State)}  {lastActivity,TimeColumnWidth}  {pausedAt,TimeColumnWidth}  {FormatDisconnect(guildState, now)}");
                }

                if (trackedGuilds.Length > 12)
                    status.AppendLine($"{Dim}+{trackedGuilds.Length - 12} more tracked guilds hidden{Reset}");
            }

            await ctx.RespondAsync($"```ansi\n{status}```").ConfigureAwait(false);
        }

        HealthInfo GetDiscordHealth(CommandContext ctx) {
            var client = ctx.Client;
            var currentUser = client.CurrentUser;
            var ping = client.Ping;
            var state = currentUser == null || ping < 0 ? "warn" : "ok";
            var color = state == "ok" ? Green : Yellow;
            var shard = client.ShardCount > 1 ? $"   shard {client.ShardId + 1}/{client.ShardCount}" : "";
            var detail = $"ping {ping,4}ms   guilds {client.Guilds.Count,3}{shard}";

            if (currentUser == null)
                detail += "   current user unavailable";

            return new HealthInfo(state, color, state == "ok", detail);
        }

        async Task<HealthInfo> GetLavalinkHealthAsync() {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try {
                var apiClient = await audioService.ApiClientProvider
                    .GetClientAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);

                var version = await apiClient
                    .RetrieveVersionAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);

                var stats = await apiClient
                    .RetrieveStatisticsAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);

                sw.Stop();

                var load = stats.ProcessorUsage.LavalinkLoad * 100;
                var detail = $"rest {sw.ElapsedMilliseconds,4}ms   players {stats.PlayingPlayers}/{stats.ConnectedPlayers}   cpu {load,5:0.0}%   v{version}";
                return new HealthInfo("ok", Green, true, detail);
            } catch (TimeoutException) {
                sw.Stop();
                return new HealthInfo("down", Red, false, $"REST timed out after {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                sw.Stop();
                return new HealthInfo("down", Red, false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        string FormatHealth(bool hasWarning) {
            return hasWarning 
                ? $"{YellowBackground} 【 DEGRADED 】{Reset}" 
                : $"{Green}● OK{Reset}";
        }

        string FormatAudioState(bool isDrifting) {
            return isDrifting
                ? FormatAnsiColumn("drift", Yellow, SummaryStateWidth)
                : FormatAnsiColumn("ok", Green, SummaryStateWidth);
        }

        static string FormatHealthColumn(string value, string color) {
            return FormatAnsiColumn(value, color, SummaryStateWidth);
        }

        string FormatWatchdogState(TimeSpan? timerAge) {
            if (timerAge == null)
                return FormatAnsiColumn("not set", Red, SummaryStateWidth);

            var color = timerAge.Value < TimeSpan.FromSeconds(90)
                ? Green
                : timerAge.Value < TimeSpan.FromMinutes(3)
                    ? Yellow
                    : Red;

            return FormatAnsiColumn("running", color, SummaryStateWidth);
        }

        static string FormatLastTimerTick(DateTime? lastTimerTick) {
            return lastTimerTick.HasValue ? lastTimerTick.Value.ToString("HH:mm:ss") : "-";
        }

        static string FormatTimerAge(TimeSpan? timerAge) {
            return timerAge.HasValue ? FormatDuration(timerAge.Value) : "-";
        }

        static string FormatSummaryLabel(string label) {
            return label.PadRight(SummaryLabelWidth);
        }

        static string FormatAnsiColumn(string value, string color, int width) {
            return $"{color}{value.PadRight(width)}{Reset}";
        }

        string FormatState(PlayerState state) {
            var label = state switch {
                PlayerState.Playing => "PLAYING",
                PlayerState.Paused => "PAUSED",
                PlayerState.NotPlaying => "IDLE",
                PlayerState.Destroyed => "DESTROYED",
                _ => state.ToString().ToUpperInvariant()
            };

            var color = state switch {
                PlayerState.Playing => Green,
                PlayerState.Paused => Yellow,
                PlayerState.Destroyed => Red,
                _ => Dim
            };

            return $"{color}{label.PadLeft(StateColumnWidth)}{Reset}";
        }

        DateTime? GetDisconnectAt(GuildActivityState state) {
            return state.State switch {
                PlayerState.Playing => null,
                PlayerState.Paused => state.LastActivity.Add(botInactivity.TsTimeoutPaused),
                _ => state.LastActivity.Add(botInactivity.TsTimeoutInactivity)
            };
        }

        string FormatDisconnect(GuildActivityState state, DateTime now) {
            var disconnectAt = GetDisconnectAt(state);
            if (disconnectAt == null)
                return "-".PadLeft(DisconnectColumnWidth);

            var remaining = disconnectAt.Value - now;
            if (remaining <= TimeSpan.Zero)
                return $"{Red}{"due now".PadLeft(DisconnectColumnWidth)}{Reset}";

            var color = remaining <= TimeSpan.FromMinutes(1)
                ? Red
                : remaining <= TimeSpan.FromMinutes(3)
                    ? Yellow
                    : Green;

            var text = $"in {FormatDuration(remaining)} ({disconnectAt.Value:HH:mm:ss})";
            return $"{color}{text.PadLeft(DisconnectColumnWidth)}{Reset}";
        }

        static string FormatDuration(TimeSpan value) {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            if (value.TotalHours >= 1)
                return $"{(int)value.TotalHours:00}h {value.Minutes:00}m";

            return $"{value.Minutes:00}m {value.Seconds:00}s";
        }

        private readonly record struct HealthInfo(string State, string Color, bool IsHealthy, string Detail);
    }
}
