using AngleSharp;
using AngleSharp.Html.Parser;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CCTavern.Player {
    internal class SunoAIParser {

        private static ILogger<SunoAIParser> _logger;

        private static ILogger<SunoAIParser> logger {
            get {
                if (_logger == null) 
                    _logger = Program.LoggerFactory.CreateLogger<SunoAIParser>();
                return _logger;
            }
        }

        public static async Task<TavernPlayerQueueItem?> GetSunoTrack(string? url) {
            if (url == null) return null;

            // Check if the url is a track url
            if (url.Trim().EndsWith(".mp3")) {
                // https://cdn1.suno.ai/{guid}.mp3
                var uri = new Uri(url);
                string songGuid = Path.GetFileNameWithoutExtension(uri.LocalPath);
                url = $"https://suno.com/song/{songGuid}";
            }

            // Confirm if the song is a redirect, its wasteful and should be rewritten later.
            url = await GetTrackInfoUrlFromSunoUrl(url);
            if (url == null) return null;

            try { return await ExtractMediaInformationFromSongUrl(url); } 
            catch (Exception) { return null; }
        }

        private static async Task<string?> GetTrackInfoUrlFromSunoUrl(string url) {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            string html = "";
            try {
                var response = await client.GetAsync(url).ConfigureAwait(false);

                // Handle redirect up to 3 times.
                const int MaxRedirects = 3;
                HttpStatusCode[] redirectCodes = [
                    HttpStatusCode.Redirect,
                    HttpStatusCode.RedirectKeepVerb,
                    HttpStatusCode.RedirectMethod,
                    HttpStatusCode.Moved,
                    HttpStatusCode.MovedPermanently,
                    HttpStatusCode.TemporaryRedirect
                ];

                // Redirect up to $MaxRedirect times.
                for (int attempt = 0; attempt < MaxRedirects; attempt++) {
                    // Not redirecting
                    if (!redirectCodes.Contains(response.StatusCode))
                        break;

                    var location = response.Headers.Location;
                    if (location == null)
                        break;

                    response = await client.GetAsync(location).ConfigureAwait(false);
                }

                Stream receiveStream;
                StreamReader readStream;

                // Check if we have a NextJS chunked redirect, because redirecting via the header is soo god damn hard.
                var finalUri = response.RequestMessage?.RequestUri?.ToString();
                if (finalUri != null && finalUri.Contains("/s/")) {
                    receiveStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    readStream = new StreamReader(receiveStream, Encoding.UTF8);
                    html = await readStream.ReadToEndAsync().ConfigureAwait(false);

                    url = ExtractShortRedirectUrl(html)!;
                    if (url != null) {
                        url = url!.Split('?')[0];

                        if (url![0] != '/') url = "/" + url;
                        return ("https://suno.com" + url);
                    }
                }
                return url;
            } catch (HttpRequestException) {
                return null;
            }
        }

        private static async Task<TavernPlayerQueueItem?> ExtractMediaInformationFromSongUrl(string url) {
            var ctx = BrowsingContext.New(Configuration.Default.WithDefaultLoader());
            var doc = await ctx.OpenAsync(url);

            var buckets = new Dictionary<int, List<string>>();
            var rxPush = new Regex(
                @"self\.__next_f\.push\(\[(\d+),\s*""(?<data>(?:\\.|[^""])*)""",
                RegexOptions.Singleline);

            // Combine all the RSC chunks into a bucket based on it's id.
            foreach (var node in doc.QuerySelectorAll("script")) {
                var m = rxPush.Match(node.TextContent);
                if (!m.Success) continue;

                int id = int.Parse(m.Groups[1].Value);
                string rawSeg = m.Groups["data"].Value; // DON'T Unescape yet, data still needs to be appended
                buckets.TryAdd(id, new());
                buckets[id].Add(rawSeg);
            }

            // each RSC chunk (0,1,5,12, …)
            foreach (var kvp in buckets) {
                string fullChunk = string.Concat(kvp.Value); 
                string unesc = Regex.Unescape(fullChunk);

                // Find and extract the "clip" object and its parent, this is the 
                // media information for the song.
                if (ExtractJsonContainingClip(unesc) is not { } json)
                    continue;

                var obj = JObject.Parse(json);
                return CreateQueueItemFromTrackInfoObject(obj);
            }

            return null;
        }

        static TavernPlayerQueueItem CreateQueueItemFromTrackInfoObject(JObject trackInfo) {
            string trackId = (string)trackInfo["clip"]!["id"]!;
            string trackName = (string)trackInfo["clip"]!["title"]!;

            string artistName = string.Empty;
            string? artistUrl = null;
            string artistImageUrl = string.Empty;

            if (trackInfo["persona"] == null) {
                artistName = (string)trackInfo["persona"]!["user_display_name"]!;
                artistImageUrl = (string)trackInfo["persona"]!["user_image_url"]!;
            } else {
                artistName = trackInfo["clip"]!["display_name"]!.ToString().Trim();
                artistImageUrl = trackInfo["clip"]!["avatar_image_url"]!.ToString().Trim();

                string artistHandle = trackInfo["clip"]!["handle"]!.ToString().Trim();
                artistUrl = $"https://suno.com/@" + artistHandle;
            }

            string imageUrl = (string)trackInfo["clip"]!["image_url"]!;
            string audioUrl = (string)trackInfo["clip"]!["audio_url"]!;

            if (string.IsNullOrWhiteSpace(trackName)) {
                if (trackInfo["clip"]?["metadata"]?["prompt"] != null) {
                    trackName = ((string?)trackInfo["clip"]?["metadata"]?["prompt"])?.Split("\n")?.FirstOrDefault() ?? "Unknown artist";
                } else if (trackInfo["clip"]?["metadata"]?["tags"] != null) {
                    trackName = (string)trackInfo["clip"]!["metadata"]!["tags"]!;
                } else {
                    trackName = "Unknown title";
                }
            }

            var trackItem = new TavernPlayerQueueItem();
            trackItem.TrackTitle = trackName;
            trackItem.TrackThumbnail = imageUrl;
            trackItem.AuthorUrl = artistUrl;
            trackItem.AuthorDisplayName = artistName;
            trackItem.AuthorAvatarUrl = artistImageUrl;
            trackItem.TrackUrl = $"https://suno.com/song/{trackId}";
            trackItem.TrackAudioUrl = audioUrl;
            trackItem.AuthorSuffix = "via Suno AI";

            return trackItem;
        }

        static string? ExtractJsonContainingClip(string flightChunk) {
            // 1. Yank headers and flatten new-lines
            var sb = new StringBuilder();
            foreach (var rawLine in flightChunk.Split('\n')) {
                var colon = rawLine.IndexOf(':');
                if (colon < 0) continue;              // malformed line
                sb.Append(rawLine[(colon + 1)..]);    // strip "header:"
            }
            var flat = sb.ToString().Replace("\r", "");   // keep \\n inside strings

            // 2. Find the first occurrence of "clip"
            var clipPos = flat.IndexOf("\"clip\"", StringComparison.Ordinal);
            if (clipPos < 0) return null;

            // 3. Scan backwards to the opening '{'
            var openIdx = flat.LastIndexOf('{', clipPos);
            if (openIdx < 0) return null;

            // 4. Forward scan with brace-depth that honours quoted strings
            bool inString = false;
            bool escape = false;
            int depth = 0;

            for (int i = openIdx; i < flat.Length; i++) {
                char c = flat[i];

                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }

                if (c == '"') inString = !inString;
                if (inString) continue;

                if (c == '{') depth++;
                if (c == '}') depth--;

                if (depth == 0)
                    return flat.Substring(openIdx, i - openIdx + 1); // complete object
            }
            return null; // unbalanced
        }


        // Compile once – thread-safe and fast.
        private static readonly Regex ShortRedirectStringSongUrlRegex = new Regex(
            @"NEXT_REDIRECT;replace;(?<url>/song/[^;\s\""']+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Returns the /song/ path (including any query-string) or <c>null</c> if none present.
        /// </summary>
        public static string? ExtractShortRedirectUrl(string pageHtml) {
            // Parse with AngleSharp
            var parser = new HtmlParser();
            var document = parser.ParseDocument(pageHtml);

            // Grab the <script> elements that contains "__next_f.push"
            var scripts = document.Scripts.Where(s => s.TextContent.Contains("NEXT_REDIRECT")).ToArray();
            if (scripts.Length == 0) return null;

            Match? m = null;

            foreach (var script in scripts) {
                var raw = script.Text;

                if (raw is null) throw new ArgumentNullException(nameof(raw));

                m = ShortRedirectStringSongUrlRegex.Match(raw);
                if (m.Success)
                    return m.Groups["url"].Value;
            }

            return m == null ? null 
                : (m.Success ? m.Groups["url"].Value : null);
        }

    }
}
