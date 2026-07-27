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

        public static Regex RgxTimestampMatch = new Regex(@"(?<ts>(?:\d{1,2}:\d{2}(?::\d{2})?))", RegexOptions.Compiled);

        private static HttpClient cli = new HttpClient();

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

            if (chapters.Count == 0)
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
            await using var ctx = new TavernContext();

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
                        Title = track.DisplayText,
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
                    var matchTs = RgxTimestampMatch.Match(line);

                    if (!matchTs.Success)
                        continue;

                    var timestamp = matchTs.Value.TryParseTimeStamp();

                    if (timestamp == null)
                        continue;

                    if (chapters.ContainsKey(timestamp.Value))
                        continue;

                    chapters.Add(
                        timestamp.Value,
                        new IVideoChapter {
                            Title = line,
                            Time = (int)timestamp.Value.TotalSeconds,
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

            await using var ctx = new TavernContext();

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
