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
    internal class YoutubeChaptersParser
    {

        private static Regex RgxTimestampMatch = new Regex(@"(?<ts>(?:[\d]{2}:[\d]{2}:[\d]{2}|[\d]{2}:[\d]{2}))", RegexOptions.Compiled);

        private static HttpClient cli = new HttpClient();

        // TEST: var chapters = await YoutubeChaptersParser.ParseChapters("vrMfm8-UBVM");
        public static async Task<(bool success, SortedList<TimeSpan, IVideoChapter>? chapters)> ParseChapters(string videoId, CancellationToken cancellationToken = default)
        {
            // snippet,chapters | if you want to get the description too. To parse it see below in commented function
            string apiEndpoint = string.Format("{0}/videos?part=snippet,chapters&id={1}", Program.Settings.YoutubeIntegration.OperationalApi, videoId);
            var response = await cli.GetAsync(apiEndpoint, cancellationToken);
            var json = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json))
                return (false, null);

            // Deserialize the json and validate it's results.
            IYoutubeRequest? yt = JsonConvert.DeserializeObject<IYoutubeRequest>(json);
            if (yt == null || yt.Error != null || yt?.Items == null || yt.Items.Count == 0) 
                return (false, null);

            var slTracks = new SortedList<TimeSpan, IVideoChapter>();
            var videoItem = yt.Items[0];
            foreach (var chapter in videoItem.Chapters.Chapters) {
                var timespan = TimeSpan.FromSeconds(chapter.Time);
                slTracks.Add(timespan, chapter);
            }

            // Attempt to parse the chapters from the description.
            if (slTracks.Count == 0) {
                var splitDescription = videoItem.Snippet.Description
                    .Split('\n')
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();

                // Get Timestamps
                foreach (var line in splitDescription) {
                    var matchTs = RgxTimestampMatch.Match(line);
                    if (matchTs.Success) {
                        // Parse into timestamp
                        var ts = matchTs.Value.TryParseTimeStamp();
                        if (ts == null) continue;

                        slTracks.Add(ts.Value, new IVideoChapter() { Title = line, Time = (int)ts.Value.TotalSeconds, Thumbnails = new List<IThumbnail>() });
                    }
                }
            }

            return (slTracks.Count != 0, slTracks);
        }

        /*
        private async Task<bool> ParsePlaylist(ulong guildId, ulong trackPosition, string videoId) {
            return null;
            
        getTimestampsFromYoutubeDescription:
            var service = YoutubeHelper.Instance.YoutubeService;
            if (service == null) return false;

            VideosResource.ListRequest listRequest = service.Videos.List("snippet,chapters");
            listRequest.Id = videoId;

            VideoListResponse response = listRequest.Execute();

            if (response.Items.Any() == false) return false;
            var videoItem = response.Items[0];
            var splitDescription = videoItem.Snippet.Description
                .Split('\n')
                .Select(x => x.Trim())
                .Where(x => !x.IsNullOrEmpty())
                .ToArray();

            SortedList<TimeSpan, string> slTracks = new SortedList<TimeSpan, string>();

            // Get Timestamps
            foreach (var line in splitDescription) {
                var matchTs = RgxTimestampMatch.Match(line);
                if (matchTs.Success) {
                    // Parse into timestamp
                    var ts = matchTs.Value.TryParseTimeStamp();
                    if (ts == null) continue;

                    slTracks.Add(ts.Value, matchTs.Value);
                }
            }

            // Check if we are still playing the same song
            var db = new TavernContext();
            var guild = await db.GetGuild(guildId);

            // Update the track list
            if (guild?.CurrentTrack == trackPosition) {
                GuildStates[guildId].PlaylistTracks = slTracks;
            }

            return false; 
        }//*/

        // IYoutubeRequest myDeserializedClass = JsonConvert.DeserializeObject<IYoutubeRequest>(myJsonResponse);
        public class IYoutubeRequest
        {
            [JsonProperty("kind")]
            public required string Kind { get; set; }

            [JsonProperty("etag")]
            public required string Etag { get; set; }

            [JsonProperty("items")]
            public List<IItem>? Items { get; set; }

            [JsonProperty("error")]
            public IError? Error { get; set; }
        }

        public class IError
        {
            [JsonProperty("code")]
            public int Code { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; } = null!;
        }

        public class IVideoChapter
        {
            [JsonProperty("title")]
            public string? Title { get; set; }

            [JsonProperty("time")]
            public int Time { get; set; }

            [JsonProperty("thumbnails")]
            public List<IThumbnail>? Thumbnails { get; set; }
        }

        public class IChapterDefinition
        {

            [JsonProperty("areAutoGenerated")]
            public bool AreAutoGenerated { get; set; }

            [JsonProperty("chapters")]
            public List<IVideoChapter> Chapters { get; set; } = null!;
        }

        public class IItem
        {
            [JsonProperty("kind")]
            public string Kind { get; set; } = null!;

            [JsonProperty("etag")]
            public string Etag { get; set; } = null!;

            [JsonProperty("id")]
            public string Id { get; set; } = null!;

            [JsonProperty("chapters")]
            public IChapterDefinition Chapters { get; set; } = null!;

            [JsonProperty("snippet")]
            public ISnippet Snippet { get; set; } = null!;
        }

        public class ISnippet
        {
            [JsonProperty("publishedAt")]
            public object PublishedAt { get; set; } = null!;

            [JsonProperty("description")]
            public string Description { get; set; } = null!;
        }

        public class IThumbnail
        {
            [JsonProperty("url")]
            public required string Url { get; set; }

            [JsonProperty("width")]
            public int Width { get; set; }

            [JsonProperty("height")]
            public int Height { get; set; }
        }
    }
}
