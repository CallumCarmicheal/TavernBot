using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Library;

public sealed class LavaLyricsResponse {
    [JsonPropertyName("sourceName")]
    public string? SourceName { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("lines")]
    public List<LavaLyricLine> Lines { get; set; } = [];

    [JsonPropertyName("plugin")]
    public Dictionary<string, object>? Plugin { get; set; }
}

public sealed class LavaLyricLine {
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("duration")]
    public long? Duration { get; set; }

    [JsonPropertyName("line")]
    public string Line { get; set; } = "";

    [JsonPropertyName("plugin")]
    public Dictionary<string, object>? Plugin { get; set; }
}

public sealed class LavaLyricsWindow {
    public LavaLyricLine? Current { get; init; }

    public IReadOnlyList<LavaLyricLine> Upcoming { get; init; } = Array.Empty<LavaLyricLine>();

    public IReadOnlyList<LavaLyricLine> All { get; init; } = Array.Empty<LavaLyricLine>();
}

public sealed class LavaLyricsProvider {
    private readonly IHttpClientFactory _httpClientFactory;

    public LavaLyricsProvider(IHttpClientFactory httpClientFactory) {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets timed lyrics for the track currently playing in the specified guild.
    ///
    /// Returns null when:
    /// - no lyrics could be found (204)
    /// - no track is currently playing (404)
    /// </summary>
    public async Task<LavaLyricsResponse?> GetCurrentLyricsAsync(string sessionId, ulong guildId, bool skipTrackSource = false, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        var httpClient = _httpClientFactory.CreateClient("LavaLyrics");

        var requestUri =
            $"v4/sessions/{Uri.EscapeDataString(sessionId)}" +
            $"/players/{guildId}/track/lyrics" +
            $"?skipTrackSource={skipTrackSource.ToString().ToLowerInvariant()}";

        using var response = await httpClient.GetAsync(
            requestUri,
            cancellationToken);

        if (response.StatusCode is
            HttpStatusCode.NoContent or
            HttpStatusCode.NotFound) {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LavaLyricsResponse>(
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the lyric currently active at playbackPosition,
    /// together with all lines beginning within lookAhead.
    ///
    /// For a 7-second Discord update loop I'd normally use
    /// 8-10 seconds of lookahead.
    /// </summary>
    public static LavaLyricsWindow GetWindow(LavaLyricsResponse? lyrics, TimeSpan playbackPosition, TimeSpan lookAhead) {
        if (lyrics?.Lines is null || lyrics.Lines.Count == 0) {
            return new LavaLyricsWindow();
        }

        var lines = lyrics.Lines
            .OrderBy(x => x.Timestamp)
            .ToArray();

        var now = (long)playbackPosition.TotalMilliseconds;
        var end = now + (long)lookAhead.TotalMilliseconds;

        var currentIndex = FindCurrentLineIndex(lines, now);

        LavaLyricLine? current = null;

        if (currentIndex >= 0) {
            current = lines[currentIndex];
        }

        var upcoming = new List<LavaLyricLine>();

        // Start after the current line. If playback hasn't reached
        // the first lyric yet, start from line zero.
        var startIndex = currentIndex >= 0
            ? currentIndex + 1
            : 0;

        for (var i = startIndex; i < lines.Length; i++) {
            var line = lines[i];

            if (line.Timestamp > end)
                break;

            if (line.Timestamp >= now)
                upcoming.Add(line);
        }

        var all = new List<LavaLyricLine>();

        if (current is not null)
            all.Add(current);

        all.AddRange(upcoming);

        return new LavaLyricsWindow {
            Current = current,
            Upcoming = upcoming,
            All = all
        };
    }

    /// <summary>
    /// Convenience helper for your Discord embed.
    /// Current lyric is prefixed with &gt;, upcoming lines are indented.
    /// </summary>
    public static string? FormatWindow(LavaLyricsResponse? lyrics, TimeSpan playbackPosition, TimeSpan lookAhead, int maxCharacters = 1000) {
        var window = GetWindow(
            lyrics,
            playbackPosition,
            lookAhead);

        if (window.All.Count == 0)
            return null;

        var result = new List<string>();

        if (window.Current is not null
            && !string.IsNullOrWhiteSpace(window.Current.Line)) {
            result.Add($"> **{window.Current.Line}**");
        }

        foreach (var line in window.Upcoming) {
            if (string.IsNullOrWhiteSpace(line.Line))
                continue;

            result.Add(line.Line);
        }

        if (result.Count == 0)
            return null;

        var text = string.Join('\n', result);

        if (text.Length <= maxCharacters)
            return text;

        return text[..Math.Max(0, maxCharacters - 3)] + "...";
    }

    private static int FindCurrentLineIndex(IReadOnlyList<LavaLyricLine> lines, long positionMs) {
        // Binary search because lyrics are timestamp ordered.
        var low = 0;
        var high = lines.Count - 1;
        var result = -1;

        while (low <= high) {
            var middle = low + ((high - low) / 2);

            if (lines[middle].Timestamp <= positionMs) {
                result = middle;
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }

        return result;
    }
}
