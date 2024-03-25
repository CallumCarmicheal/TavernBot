using CCTavern.Player;

using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

using Lavalink4NET;
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

        [Command("status"), Aliases("stats")]
        [Description("View the status of the inactivity module")]
        [RequireGuild]
        public async Task StatusInactivity(CommandContext ctx) {
            // 20
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

            status += $"\n{"Guild Id",20} {"Last Checkin",12} {"Paused Since",12} {"State",12}\n";
            status += $"{"".PadRight(20, '-')} {"".PadRight(12, '-')} {"".PadRight(12, '-')} {"".PadRight(12, '-')}\n";
            foreach (var row in botInactivity.lastActivityTracker) {
                var guildId = row.Key;
                var playingState = row.Value;

                var lastActivity = playingState.LastActivity.ToString("HH:mm:ss");
                var pausedDate   = playingState.PausedDate.HasValue ? playingState.PausedDate.Value.ToString("HH:mm:ss") : "***";
                status += $"{guildId,20} {lastActivity,12} {pausedDate,12} {playingState.State, 12}";
            }
            status += "```";

            await ctx.RespondAsync(status).ConfigureAwait(false);
        }
    }
}
