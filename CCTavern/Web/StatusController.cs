using CCTavern.Commands;
using CCTavern.Player;

using DSharpPlus;

using Lavalink4NET;
using Lavalink4NET.Players;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Web;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase {
    private const string HtmlContentType = "text/html; charset=utf-8";
    private static readonly TimeSpan LavalinkRequestTimeout = TimeSpan.FromSeconds(3);

    private readonly MusicBotHelper mbHelper;
    private readonly BotInactivityImplementation botInactivity;
    private readonly IAudioService audioService;
    private readonly DiscordClient discordClient;
    private readonly ILogger<StatusCommandModule> logger;

    public StatusController(
        MusicBotHelper mbHelper,
        BotInactivityImplementation botInactivity,
        IAudioService audioService,
        DiscordClient discordClient,
        ILogger<StatusCommandModule> logger
    ) {
        this.mbHelper = mbHelper;
        this.botInactivity = botInactivity;
        this.audioService = audioService;
        this.discordClient = discordClient;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<BotStatusResponse>> Status(CancellationToken cancellationToken) {
        try {
            return Ok(await CreateStatusSnapshotAsync(cancellationToken).ConfigureAwait(false));
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            logger.LogError(exception, "Failed to collect the HTTP status response.");
            return Problem(
                statusCode: 500,
                title: "Unable to collect the application status.");
        }
    }

    [HttpGet("view")]
    public async Task<IActionResult> StatusView(CancellationToken cancellationToken) {
        try {
            var snapshot = await CreateStatusSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return Content(RenderStatusPage(snapshot), HtmlContentType);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            logger.LogError(exception, "Failed to render the HTTP status page.");
            return new ContentResult {
                Content = "Unable to render the status page.",
                ContentType = "text/plain; charset=utf-8",
                StatusCode = 500
            };
        }
    }

    private async Task<BotStatusResponse> CreateStatusSnapshotAsync(CancellationToken cancellationToken) {
        var now = DateTime.Now;
        var playersActive = audioService.Players.Players.Count();
        var trackedGuilds = botInactivity.lastActivityTracker.ToArray();
        var lastTimerTick = botInactivity.GetLastTimerTick();
        var timerAge = lastTimerTick.HasValue ? now - lastTimerTick.Value : (TimeSpan?)null;
        var isDrifting = playersActive != trackedGuilds.Length;
        var discordHealth = GetDiscordHealth();
        var lavalinkHealth = await GetLavalinkHealthAsync(cancellationToken).ConfigureAwait(false);
        var watchdogIsStale = timerAge == null || timerAge.Value >= TimeSpan.FromMinutes(3);
        var hasWarning = isDrifting
            || !discordHealth.IsHealthy
            || !lavalinkHealth.IsHealthy
            || watchdogIsStale;

        var guilds = trackedGuilds
            .OrderBy(x => x.Value.State == PlayerState.Playing)
            .ThenBy(x => GetDisconnectAt(x.Value) ?? DateTime.MaxValue)
            .ThenBy(x => x.Value.LastActivity)
            .Select(x => CreateGuildStatus(x.Key, x.Value, now))
            .ToArray();

        return new BotStatusResponse(
            now,
            hasWarning ? "degraded" : "ok",
            !hasWarning,
            discordHealth,
            lavalinkHealth,
            new AudioStatus(
                isDrifting ? "drift" : "ok",
                !isDrifting,
                playersActive,
                trackedGuilds.Length),
            new WatchdogStatus(
                timerAge == null ? "not set" : "running",
                !watchdogIsStale,
                watchdogIsStale,
                lastTimerTick,
                timerAge,
                FormatLastTimerTick(lastTimerTick),
                FormatTimerAge(timerAge)),
            new TimeoutStatus(
                botInactivity.TsTimeoutInactivity,
                botInactivity.TsTimeoutPaused,
                botInactivity.TsTimeoutInactivity.ToDynamicTimestamp(),
                botInactivity.TsTimeoutPaused.ToDynamicTimestamp()),
            guilds);
    }

    private DiscordStatus GetDiscordHealth() {
        var currentUser = discordClient.CurrentUser;
        var ping = discordClient.Ping;
        var isHealthy = currentUser != null && ping >= 0;
        var shardNumber = discordClient.ShardCount > 1 ? discordClient.ShardId + 1 : (int?)null;
        var shardCount = discordClient.ShardCount > 1 ? discordClient.ShardCount : (int?)null;
        var shard = shardCount.HasValue ? $"   shard {shardNumber}/{shardCount}" : "";
        var detail = $"ping {ping,4}ms   guilds {discordClient.Guilds.Count,3}{shard}";

        if (currentUser == null)
            detail += "   current user unavailable";

        return new DiscordStatus(
            isHealthy ? "ok" : "warn",
            isHealthy,
            ping,
            discordClient.Guilds.Count,
            shardNumber,
            shardCount,
            detail);
    }

    private async Task<LavalinkStatus> GetLavalinkHealthAsync(CancellationToken cancellationToken) {
        var stopwatch = Stopwatch.StartNew();

        try {
            var apiClient = await audioService.ApiClientProvider
                .GetClientAsync()
                .AsTask()
                .WaitAsync(LavalinkRequestTimeout, cancellationToken)
                .ConfigureAwait(false);

            var version = await apiClient
                .RetrieveVersionAsync()
                .AsTask()
                .WaitAsync(LavalinkRequestTimeout, cancellationToken)
                .ConfigureAwait(false);

            var statistics = await apiClient
                .RetrieveStatisticsAsync()
                .AsTask()
                .WaitAsync(LavalinkRequestTimeout, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            var load = statistics.ProcessorUsage.LavalinkLoad * 100;
            var detail = $"rest {stopwatch.ElapsedMilliseconds,4}ms   players {statistics.PlayingPlayers}/{statistics.ConnectedPlayers}   cpu {load,5:0.0}%   v{version}";

            return new LavalinkStatus(
                "ok",
                true,
                stopwatch.ElapsedMilliseconds,
                statistics.PlayingPlayers,
                statistics.ConnectedPlayers,
                load,
                version.ToString(),
                detail);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (TimeoutException) {
            stopwatch.Stop();
            var detail = $"REST timed out after {stopwatch.ElapsedMilliseconds}ms";
            return new LavalinkStatus("down", false, stopwatch.ElapsedMilliseconds, null, null, null, null, detail);
        } catch (Exception exception) {
            stopwatch.Stop();
            var detail = $"{exception.GetType().Name}: {exception.Message}";
            return new LavalinkStatus("down", false, stopwatch.ElapsedMilliseconds, null, null, null, null, detail);
        }
    }

    private GuildStatus CreateGuildStatus(ulong guildId, GuildActivityState state, DateTime now) {
        var disconnectAt = GetDisconnectAt(state);
        var remaining = disconnectAt.HasValue ? disconnectAt.Value - now : (TimeSpan?)null;

        if (remaining <= TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return new GuildStatus(
            guildId,
            state.State.ToString(),
            state.LastActivity,
            state.PausedDate,
            disconnectAt,
            disconnectAt.HasValue && disconnectAt.Value <= now,
            remaining,
            FormatDisconnect(disconnectAt, now));
    }

    private DateTime? GetDisconnectAt(GuildActivityState state) {
        return state.State switch {
            PlayerState.Playing => null,
            PlayerState.Paused => state.LastActivity.Add(botInactivity.TsTimeoutPaused),
            _ => state.LastActivity.Add(botInactivity.TsTimeoutInactivity)
        };
    }

    private static string RenderStatusPage(BotStatusResponse snapshot) {
        const int maximumVisibleGuilds = 12;
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
        status.AppendLine("<main class=\"container py-4\">");
        status.Append("<h1>Music Bot Status <span class=\"badge text-bg-")
            .Append(snapshot.IsHealthy ? "success" : "warning")
            .Append("\">")
            .Append(snapshot.IsHealthy ? "OK" : "DEGRADED")
            .AppendLine("</span></h1>");

        status.AppendLine("<table class=\"table table-sm align-middle\"><tbody>");
        AppendSummaryRow(status, "Discord", snapshot.Discord.State, snapshot.Discord.IsHealthy ? "success" : "warning", snapshot.Discord.Detail);
        AppendSummaryRow(status, "Lavalink", snapshot.Lavalink.State, snapshot.Lavalink.IsHealthy ? "success" : "danger", snapshot.Lavalink.Detail);
        AppendSummaryRow(
            status,
            "Audio",
            snapshot.Audio.State,
            snapshot.Audio.IsHealthy ? "success" : "warning",
            $"local {snapshot.Audio.ActivePlayers,3}   tracked {snapshot.Audio.TrackedGuilds,3}");
        AppendSummaryRow(
            status,
            "Watchdog",
            snapshot.Watchdog.State,
            WatchdogClass(snapshot.Watchdog),
            $"last tick {snapshot.Watchdog.LastTimerTickDisplay,8}   age {snapshot.Watchdog.AgeDisplay,8}");
        AppendSummaryRow(
            status,
            "Timeouts",
            "",
            "secondary",
            $"idle {snapshot.Timeouts.InactivityDisplay,8}   paused {snapshot.Timeouts.PausedDisplay,8}");
        status.AppendLine("</tbody></table>");

        if (snapshot.Guilds.Count == 0) {
            status.AppendLine("<p class=\"text-secondary\">No guilds are currently tracked by the inactivity manager.</p>");
        } else {
            status.AppendLine("<table class=\"table table-sm table-striped align-middle\">");
            status.AppendLine("<thead><tr><th>Guild</th><th>State</th><th>Last</th><th>Paused</th><th>Disconnect</th></tr></thead>");
            status.AppendLine("<tbody>");

            foreach (var guild in snapshot.Guilds.Take(maximumVisibleGuilds)) {
                status.AppendLine("<tr>");
                status.Append("<td>").Append(HtmlEncode(guild.GuildId)).AppendLine("</td>");
                status.Append("<td><span class=\"badge text-bg-")
                    .Append(StateClass(guild.State))
                    .Append("\">")
                    .Append(HtmlEncode(FormatState(guild.State)))
                    .AppendLine("</span></td>");
                status.Append("<td>").Append(HtmlEncode(guild.LastActivity.ToString("HH:mm:ss", CultureInfo.InvariantCulture))).AppendLine("</td>");
                status.Append("<td>")
                    .Append(HtmlEncode(guild.PausedSince?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "-"))
                    .AppendLine("</td>");
                status.Append("<td class=\"")
                    .Append(DisconnectClass(guild))
                    .Append("\">")
                    .Append(HtmlEncode(guild.DisconnectDisplay))
                    .AppendLine("</td>");
                status.AppendLine("</tr>");
            }

            status.AppendLine("</tbody></table>");

            if (snapshot.Guilds.Count > maximumVisibleGuilds) {
                status.Append("<p class=\"text-secondary\">+")
                    .Append(HtmlEncode(snapshot.Guilds.Count - maximumVisibleGuilds))
                    .AppendLine(" more tracked guilds hidden</p>");
            }
        }

        status.AppendLine("</main>");
        status.AppendLine("</body>");
        status.AppendLine("</html>");
        return status.ToString();
    }

    private static void AppendSummaryRow(
        StringBuilder status,
        string label,
        string state,
        string stateClass,
        string detail
    ) {
        status.Append("<tr><th scope=\"row\">").Append(HtmlEncode(label)).Append("</th><td>");

        if (!string.IsNullOrEmpty(state)) {
            status.Append("<span class=\"badge text-bg-")
                .Append(stateClass)
                .Append("\">")
                .Append(HtmlEncode(state))
                .Append("</span>");
        }

        status.Append("</td><td><code style=\"white-space: pre\">")
            .Append(HtmlEncode(detail))
            .AppendLine("</code></td></tr>");
    }

    private static string WatchdogClass(WatchdogStatus watchdog) {
        if (watchdog.Age == null || watchdog.Age >= TimeSpan.FromMinutes(3))
            return "danger";

        return watchdog.Age >= TimeSpan.FromSeconds(90) ? "warning" : "success";
    }

    private static string DisconnectClass(GuildStatus guild) {
        if (guild.DisconnectAt == null)
            return "text-secondary";
        if (guild.DisconnectIsDue || guild.DisconnectRemaining <= TimeSpan.FromMinutes(1))
            return "text-danger";
        if (guild.DisconnectRemaining <= TimeSpan.FromMinutes(3))
            return "text-warning";

        return "text-success";
    }

    private static string StateClass(string state) {
        return state switch {
            nameof(PlayerState.Playing) => "success",
            nameof(PlayerState.Paused) => "warning",
            nameof(PlayerState.Destroyed) => "danger",
            _ => "secondary"
        };
    }

    private static string FormatState(string state) {
        return state switch {
            nameof(PlayerState.Playing) => "PLAYING",
            nameof(PlayerState.Paused) => "PAUSED",
            nameof(PlayerState.NotPlaying) => "IDLE",
            nameof(PlayerState.Destroyed) => "DESTROYED",
            _ => state.ToUpperInvariant()
        };
    }

    private static string FormatLastTimerTick(DateTime? lastTimerTick)
        => lastTimerTick.HasValue ? lastTimerTick.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "-";

    private static string FormatTimerAge(TimeSpan? timerAge)
        => timerAge.HasValue ? FormatDuration(timerAge.Value) : "-";

    private static string FormatDisconnect(DateTime? disconnectAt, DateTime now) {
        if (disconnectAt == null)
            return "-";

        var remaining = disconnectAt.Value - now;
        if (remaining <= TimeSpan.Zero)
            return "due now";

        return $"in {FormatDuration(remaining)} ({disconnectAt.Value:HH:mm:ss})";
    }

    private static string FormatDuration(TimeSpan value) {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;

        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours:00}h {value.Minutes:00}m";

        return $"{value.Minutes:00}m {value.Seconds:00}s";
    }

    private static string HtmlEncode(object? value)
        => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
}

public sealed record BotStatusResponse(
    DateTime GeneratedAt,
    string State,
    bool IsHealthy,
    DiscordStatus Discord,
    LavalinkStatus Lavalink,
    AudioStatus Audio,
    WatchdogStatus Watchdog,
    TimeoutStatus Timeouts,
    IReadOnlyList<GuildStatus> Guilds);

public sealed record DiscordStatus(
    string State,
    bool IsHealthy,
    int PingMilliseconds,
    int GuildCount,
    int? ShardNumber,
    int? ShardCount,
    string Detail);

public sealed record LavalinkStatus(
    string State,
    bool IsHealthy,
    long RestLatencyMilliseconds,
    int? PlayingPlayers,
    int? ConnectedPlayers,
    double? CpuLoadPercent,
    string? Version,
    string Detail);

public sealed record AudioStatus(
    string State,
    bool IsHealthy,
    int ActivePlayers,
    int TrackedGuilds);

public sealed record WatchdogStatus(
    string State,
    bool IsHealthy,
    bool IsStale,
    DateTime? LastTimerTick,
    TimeSpan? Age,
    string LastTimerTickDisplay,
    string AgeDisplay);

public sealed record TimeoutStatus(
    TimeSpan Inactivity,
    TimeSpan Paused,
    string InactivityDisplay,
    string PausedDisplay);

public sealed record GuildStatus(
    ulong GuildId,
    string State,
    DateTime LastActivity,
    DateTime? PausedSince,
    DateTime? DisconnectAt,
    bool DisconnectIsDue,
    TimeSpan? DisconnectRemaining,
    string DisconnectDisplay);