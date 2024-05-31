using CCTavern.Commands;
using CCTavern.Player;

using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

using Lavalink4NET;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Web {
    internal class StatusController : WebApiController {
        private readonly MusicBotHelper mbHelper;
        private readonly BotInactivityImplementation botInactivity;
        private readonly IAudioService audioService;
        private readonly ILogger<StatusCommandModule> logger;

        public StatusController(MusicBotHelper mbHelper, BotInactivityImplementation botInactivity, IAudioService audioService, ILogger<StatusCommandModule> logger) {
            this.mbHelper = mbHelper;
            this.botInactivity = botInactivity;
            this.audioService = audioService;
            this.logger = logger;
        }

        [Route(HttpVerbs.Get, "/")]
        public async Task Status() {
            var ctx = HttpContext;
            string status = "";

            var countOfPlayers = audioService.Players.Players.Count();
            status += "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css\" integrity=\"sha384-T3c6CoIi6uLrA9TneNEoa7RxnatzjcDSCmG1MXxSR1GAsXEV/Dwwykc2MPK8M2HN\" crossorigin=\"anonymous\">";
            status += "<pre>";
            status += $"Players Active: {countOfPlayers}\n";

            status += $"\nGuild Timeout with {botInactivity.lastActivityTracker.Keys.Count} guilds.\n";

            var timerTick = botInactivity.GetLastTimerTick();
            if (timerTick == null)
                status += $"Last Timer Tick:  | *Timer has not processed a tick*\n";
            else status += $"Last Timer Tick:  | {timerTick.Value:dd/MM/yyyy HH:mm:ss}\n";
            status += $"Inactivity timer: | {botInactivity.TsTimeoutInactivity.ToDynamicTimestamp()}\n";
            status += $"Paused timer:     | {botInactivity.TsTimeoutPaused.ToDynamicTimestamp()}\n";
            status += "</pre>";

            status += "<table class=\"table\">";
            status += "<thead> <tr>";
            status += "<td>Guild Id</td>";
            status += "<td>Last Checkin</td>";
            status += "<td>Pasued Since</td>";
            status += "<td>State</td>";
            status += "</tr> </thead>";

            status += "<tbody>";

            foreach (var row in botInactivity.lastActivityTracker) {
                var guildId = row.Key;
                var playingState = row.Value;

                var lastActivity = playingState.LastActivity.ToString("HH:mm:ss");
                var pausedDate = playingState.PausedDate.HasValue ? playingState.PausedDate.Value.ToString("HH:mm:ss") : "***";

                status += $"<td>{guildId}</td> <td>{lastActivity}</td> <td>{pausedDate}</td> <td>{playingState.State}</td>";
            }

            status += "</tbody> </table>";

            await ctx.SendStringAsync(status
                + "<script>var fn = () => window.location = window.location; setTimeout(fn, 1*1000);</script>"
                , "text/html", Encoding.UTF8);
        }

    }
}
