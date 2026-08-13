using CCTavern.Database;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static CCTavern.Player.YoutubeChaptersParser;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CCTavern.Player
{
    public record struct YoutubeChapterDetails(bool success, SortedList<TimeSpan, IVideoChapter>? chapters);

    public class YoutubeChaptersParser {

        public static readonly Regex RgxTimestampMatch = new(
            @"(?<![\p{L}\p{N}_:])(?<ts>(?:\d+:[0-5]\d:[0-5]\d|\d+:[0-5]\d))(?![\p{L}\p{N}_:])",
            RegexOptions.Compiled);

        private static readonly Regex RgxRepeatedWhitespace = new(
            @"\s+",
            RegexOptions.Compiled);

        private static readonly char[] ChapterTitleSeparators = [
            ' ', '\t', '-', '–', '—', '|', ':', ';', '·', '•', '.', ',', '/', '\\',
            '(', ')', '[', ']', '{', '}'
        ];

        private static HttpClient cli = new HttpClient();

        public static bool TryParseChapterLine(
            string line,
            out TimeSpan timestamp,
            out string displayText
        ) {
            timestamp = default;
            displayText = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            var timestampMatch = RgxTimestampMatch.Match(line);
            if (!timestampMatch.Success)
                return false;

            if (!TryParseTimestamp(timestampMatch.Groups["ts"].Value, out timestamp))
                return false;

            displayText = RemoveTimestampFromTitle(line, timestampMatch);
            if (string.IsNullOrWhiteSpace(displayText))
                return false;

            return true;
        }

        private static bool TryParseTimestamp(string value, out TimeSpan timestamp) {
            timestamp = default;
            var components = value.Split(':');

            if (components.Length is not (2 or 3))
                return false;

            if (!long.TryParse(components[^1], out var seconds)
                    || !long.TryParse(components[^2], out var minutes))
                return false;

            long hours = 0;
            if (components.Length == 3
                    && !long.TryParse(components[0], out hours))
                return false;

            try {
                var totalSeconds = checked(
                    checked(hours * 60 * 60)
                    + checked(minutes * 60)
                    + seconds);

                timestamp = TimeSpan.FromSeconds(totalSeconds);
                return true;
            } catch (OverflowException) {
                return false;
            }
        }

        public static string RemoveTimestampFromTitle(string title) {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var timestampMatch = RgxTimestampMatch.Match(title);
            return timestampMatch.Success
                ? RemoveTimestampFromTitle(title, timestampMatch)
                : title.Trim();
        }

        private static string RemoveTimestampFromTitle(string title, Match timestampMatch) {
            var cleanedTitle = title.Remove(timestampMatch.Index, timestampMatch.Length);
            cleanedTitle = RgxRepeatedWhitespace.Replace(cleanedTitle, " ");
            return cleanedTitle.Trim(ChapterTitleSeparators);
        }

        // TEST: var chapters = await YoutubeChaptersParser.ParseChapters("vrMfm8-UBVM");
        public static async Task<YoutubeChapterDetails> ParseChapters(string videoId, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(videoId))
                throw new ArgumentException(
                    "A YouTube video ID is required.",
                    nameof(videoId));

            cancellationToken.ThrowIfCancellationRequested();

            // 1. Check the database.
            var chapters = await LoadChaptersFromDatabase(
                videoId,
                cancellationToken);

            if (chapters.Count > 0)
                return new(true, chapters);

            // 2. Query and parse YouTube.
            chapters = await GetChaptersFromYoutube(
                videoId,
                cancellationToken);

            // Do not store if there is only 1 chapter. 
            if (chapters.Count < 1)
                return new(false, null);

            // 3. Save the parsed chapters.
            await SaveChaptersToDatabase(
                videoId,
                chapters,
                cancellationToken);

            return new(true, chapters);
        }

        private static async Task<SortedList<TimeSpan, IVideoChapter>> LoadChaptersFromDatabase(
            string videoId,
            CancellationToken cancellationToken = default
        ) {
            await using var ctx = await Program.DatabaseFactory.CreateDbContextAsync(cancellationToken);

            var tracks = await ctx.TrackPlaylistPositions
                .AsNoTracking()
                .Where(track =>
                    track.TrackSource == "youtube" &&
                    track.TrackSourceId == videoId)
                .OrderBy(track => track.Position)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var chapters = new SortedList<TimeSpan, IVideoChapter>();

            foreach (var track in tracks) {
                if (chapters.ContainsKey(track.Position))
                    continue;

                chapters.Add(
                    track.Position,
                    new IVideoChapter {
                        Title = RemoveTimestampFromTitle(track.DisplayText),
                        Time = (int)track.Position.TotalSeconds,
                        Thumbnails = new List<IThumbnail>()
                    });
            }

            return chapters;
        }

        private static async Task<SortedList<TimeSpan, IVideoChapter>> GetChaptersFromYoutube(
            string videoId,
            CancellationToken cancellationToken = default
        ) {
            var chapters = new SortedList<TimeSpan, IVideoChapter>();

            string apiEndpoint = string.Format(
                "{0}/videos?part=snippet,chapters&id={1}",
                Program.Settings.YoutubeIntegration.OperationalApi,
                videoId);

            using var response = await cli.GetAsync(
                apiEndpoint,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return chapters;

            var json = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json))
                return chapters;

            var youtubeResponse =
                JsonConvert.DeserializeObject<IYoutubeRequest>(json);

            if (youtubeResponse == null ||
                youtubeResponse.Error != null ||
                youtubeResponse.Items == null ||
                youtubeResponse.Items.Count == 0) {
                return chapters;
            }

            var videoItem = youtubeResponse.Items[0];

            // First try YouTube's chapter data.
            if (videoItem.Chapters?.Chapters != null) {
                foreach (var chapter in videoItem.Chapters.Chapters) {
                    var position = TimeSpan.FromSeconds(chapter.Time);

                    if (chapters.ContainsKey(position))
                        continue;

                    chapters.Add(position, chapter);
                }
            }

            // Fall back to timestamps in the description.
            if (chapters.Count == 0 &&
                !string.IsNullOrWhiteSpace(videoItem.Snippet?.Description)) {
                var descriptionLines = videoItem.Snippet.Description
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line));

                foreach (var line in descriptionLines) {
                    if (!TryParseChapterLine(line, out var timestamp, out var displayText))
                        continue;

                    if (chapters.ContainsKey(timestamp))
                        continue;

                    chapters.Add(
                        timestamp,
                        new IVideoChapter {
                            Title = displayText,
                            Time = (int)timestamp.TotalSeconds,
                            Thumbnails = new List<IThumbnail>()
                        });
                }
            }

            return chapters;
        }

        private static async Task SaveChaptersToDatabase(
            string videoId,
            SortedList<TimeSpan, IVideoChapter> chapters,
            CancellationToken cancellationToken = default
        ) {
            if (chapters.Count == 0)
                return;

            await using var ctx = await Program.DatabaseFactory.CreateDbContextAsync(cancellationToken);

            var existingPositions = await ctx.TrackPlaylistPositions
                .Where(track =>
                    track.TrackSource == "youtube" &&
                    track.TrackSourceId == videoId)
                .Select(track => track.Position)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingPositionSet = existingPositions.ToHashSet();

            foreach (var chapter in chapters) {
                cancellationToken.ThrowIfCancellationRequested();

                if (!existingPositionSet.Add(chapter.Key))
                    continue;

                ctx.TrackPlaylistPositions.Add(
                    new TrackPlaylistPosition {
                        TrackSource = "youtube",
                        TrackSourceId = videoId,
                        Position = chapter.Key,
                        DisplayText = chapter.Value.Title ?? string.Empty
                    });
            }

            if (!ctx.ChangeTracker.HasChanges())
                return;

            try {
                await ctx.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
            } catch (DbUpdateException) {
                /*
                 * TODO: Handle concurrent exceptions like when a unique 
                 * index may reject inserts when another request saves the 
                 * same chapters concurrently. The parsed chapters are still 
                 * valid, so duplicate inserts can be ignored here.
                 */
            }
        }

        // IYoutubeRequest myDeserializedClass = JsonConvert.DeserializeObject<IYoutubeRequest>(myJsonResponse);
        public class IYoutubeRequest
        {
            [JsonProperty("kind")]
            public string Kind { get; set; }

            [JsonProperty("etag")]
            public string Etag { get; set; }

            [JsonProperty("items")]
            public List<IItem> Items { get; set; }

            [JsonProperty("error")]
            public IError Error { get; set; }
        }

        public class IError
        {
            [JsonProperty("code")]
            public int Code { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }

        public class IVideoChapter
        {
            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("time")]
            public int Time { get; set; }

            [JsonProperty("thumbnails")]
            public List<IThumbnail> Thumbnails { get; set; }
        }

        public class IChapterDefinition
        {

            [JsonProperty("areAutoGenerated")]
            public bool AreAutoGenerated { get; set; }

            [JsonProperty("chapters")]
            public List<IVideoChapter> Chapters { get; set; }
        }

        public class IItem
        {
            [JsonProperty("kind")]
            public string Kind { get; set; }

            [JsonProperty("etag")]
            public string Etag { get; set; }

            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("chapters")]
            public IChapterDefinition Chapters { get; set; }

            [JsonProperty("snippet")]
            public ISnippet Snippet { get; set; }
        }

        public class ISnippet
        {
            [JsonProperty("publishedAt")]
            public object PublishedAt { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }
        }

        public class IThumbnail
        {
            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }
        }
    }
}
