using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using Lavalink4NET.Players;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CCTavern.Player {
    internal class SunoAIParser {

        public static async Task<TavernPlayerQueueItem?> GetSunoTrack(string? url) {
            if (url == null) return null;

            // Check if the url is a track url
            if (url.Trim().EndsWith(".mp3")) {
                // https://cdn1.suno.ai/{guid}.mp3
                var uri = new Uri(url);
                string songGuid = Path.GetFileNameWithoutExtension(uri.LocalPath);
                url = $"https://suno.com/song/{songGuid}";
            }

            try {
                // 1. Load your HTML (from string or file)
                using var client = new HttpClient();
                // (Optional) set a timeout, default headers, etc.
                client.Timeout = TimeSpan.FromSeconds(10);

                string html = "";

                try {
                    html = await client.GetStringAsync(url);
                } catch (HttpRequestException e) {
                    Console.WriteLine("Failed to request html, " + e.ToString());
                    return null;
                }

                // 2. Parse with AngleSharp
                var parser = new HtmlParser();
                var document = parser.ParseDocument(html);

                // 3. Grab the <script> elements that contains "__next_f.push"
                var scripts = document.Scripts
                                     .Where(s => s.TextContent.Contains("__next_f.push"));

                IHtmlScriptElement? script = null;
                const string marker = "\"5:[";
                int idx = -1;
                string text = string.Empty;

                foreach (var scriptElement in scripts) {
                    if (scriptElement == null)
                        throw new Exception("Could not find the __next_f.push script.");

                    text = scriptElement.TextContent;

                    // 4. Find the JSON‐array payload marker
                    idx = text.IndexOf(marker, StringComparison.Ordinal);
                    if (idx >= 0)
                        script = scriptElement;
                }

                // 5. Walk forward from the '[' and count brackets until balanced
                int start = idx + marker.Length - 1; // points at the '['
                int depth = 0, end = -1;
                for (int i = start; i < text.Length; i++) {
                    if (text[i] == '[')
                        depth++;

                    else if (text[i] == ']') {
                        depth--;

                        if (depth == 0) {
                            end = i;
                            break;
                        }
                    }
                }

                if (end < 0)
                    throw new Exception("Could not find end of JSON array.");

                // 6. Extract the slice and un‐escape the JS string literal
                var rawJson = text.Substring(start, end - start + 1);
                var unescapedJson = Regex.Unescape(rawJson);

                // 7. Parse as a JArray
                var arr = JArray.Parse(unescapedJson);

                // 8. Our payload is in element #3
                var obj = (JObject)arr[3];

                // 9. Pull out the fields you want:
                string trackId = (string)obj["clip"]!["id"]!;
                string trackName = (string)obj["clip"]!["title"]!;
                string artistName = (string)obj["persona"]!["user_display_name"]!;
                string artistImageUrl = (string)obj["persona"]!["user_image_url"]!;
                string imageUrl = (string)obj["clip"]!["image_url"]!;
                string audioUrl = (string)obj["clip"]!["audio_url"]!;

                var trackItem = new TavernPlayerQueueItem();
                trackItem.TrackTitle = trackName;
                trackItem.TrackThumbnail = imageUrl;
                trackItem.AuthorDisplayName = artistName + " via Suno AI";
                trackItem.AuthorAvatarUrl = artistImageUrl;
                trackItem.TrackUrl = $"https://suno.com/song/{trackId}";
                trackItem.TrackAudioUrl = audioUrl;

                return trackItem;
            } catch {
                return null;
            }
        }
    }
}
