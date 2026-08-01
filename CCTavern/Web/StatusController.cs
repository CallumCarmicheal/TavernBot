using CCTavern.Commands;
using CCTavern.Player;

using Lavalink4NET;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace CCTavern.Web;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase {
    private const string HtmlContentType = "text/html; charset=utf-8";

    private readonly MusicBotHelper mbHelper;
    private readonly BotInactivityImplementation botInactivity;
    private readonly IAudioService audioService;
    private readonly ILogger<StatusCommandModule> logger;

    public StatusController(
        MusicBotHelper mbHelper,
        BotInactivityImplementation botInactivity,
        IAudioService audioService,
        ILogger<StatusCommandModule> logger
    ) {
        this.mbHelper = mbHelper;
        this.botInactivity = botInactivity;
        this.audioService = audioService;
        this.logger = logger;
    }

    [HttpGet]
    public IActionResult Status() {
        try {
            var activitySnapshot = botInactivity.lastActivityTracker.ToArray();
            var countOfPlayers = audioService.Players.Players.Count();
            var timerTick = botInactivity.GetLastTimerTick();

            var status = new StringBuilder();
            status.AppendLine("<!DOCTYPE html>");
            status.AppendLine("<html lang=\"en\">");
            status.AppendLine("<head>");
            status.AppendLine("<meta charset=\"utf-8\">");
            status.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            status.AppendLine("<meta http-equiv=\"refresh\" content=\"1\">");
            status.AppendLine("<title>CCTavern Status</title>");
            status.AppendLine("<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css\" integrity=\"sha384-T3c6CoIi6uLrA9TneNEoa7RxnatzjcDSCmG1MXxSR1GAsXEV/Dwwykc2MPK8M2HN\" crossorigin=\"anonymous\">");
            status.AppendLine("</head>");
            status.AppendLine("<body>");
            status.AppendLine("<pre>");

            status.Append("Players Active: ").Append(HtmlEncode(countOfPlayers)).AppendLine();
            status.AppendLine();

            status.Append("Guild Timeout with ").Append(HtmlEncode(activitySnapshot.Length)).AppendLine(" guilds.");
            status.Append("Last Timer Tick:  | ");
            status.AppendLine(timerTick.HasValue
                ? HtmlEncode(timerTick.Value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture))
                : "*Timer has not processed a tick*");
            status.Append("Inactivity timer: | ")
                .Append(HtmlEncode(botInactivity.TsTimeoutInactivity.ToDynamicTimestamp()))
                .AppendLine();
            status.Append("Paused timer:     | ")
                .Append(HtmlEncode(botInactivity.TsTimeoutPaused.ToDynamicTimestamp()))
                .AppendLine();
            status.AppendLine("</pre>");

            status.AppendLine("<table class=\"table\">");
            status.AppendLine("<thead><tr>");
            status.AppendLine("<th>Guild Id</th>");
            status.AppendLine("<th>Last Checkin</th>");
            status.AppendLine("<th>Paused Since</th>");
            status.AppendLine("<th>State</th>");
            status.AppendLine("</tr></thead>");
            status.AppendLine("<tbody>");

            foreach (var row in activitySnapshot) {
                var playingState = row.Value;
                var lastActivity = playingState.LastActivity.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                var pausedDate = playingState.PausedDate.HasValue
                    ? playingState.PausedDate.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : "***";

                status.AppendLine("<tr>");
                status.Append("<td>").Append(HtmlEncode(row.Key)).AppendLine("</td>");
                status.Append("<td>").Append(HtmlEncode(lastActivity)).AppendLine("</td>");
                status.Append("<td>").Append(HtmlEncode(pausedDate)).AppendLine("</td>");
                status.Append("<td>").Append(HtmlEncode(playingState.State)).AppendLine("</td>");
                status.AppendLine("</tr>");
            }

            status.AppendLine("</tbody>");
            status.AppendLine("</table>");
            status.AppendLine("</body>");
            status.AppendLine("</html>");

            return Content(status.ToString(), HtmlContentType);
        } catch (Exception exception) {
            logger.LogError(exception, "Failed to render the HTTP status page.");
            return new ContentResult {
                Content = "Unable to render the status page.",
                ContentType = "text/plain; charset=utf-8",
                StatusCode = 500
            };
        }
    }

    private static string HtmlEncode(object? value)
        => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
}