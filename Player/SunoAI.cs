using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using CCTavern.Logger;

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
                // Load your HTML (from string or file)
                using var client = new HttpClient();
                // (Optional) set a timeout, default headers, etc.
                client.Timeout = TimeSpan.FromSeconds(10);

                string html = "";

                try {
                    html = await client.GetStringAsync(url).ConfigureAwait(false);
                } catch (HttpRequestException e) {
                    Console.WriteLine("Failed to request html, " + e.ToString());
                    return null;
                }

                // Parse with AngleSharp
                var parser = new HtmlParser();
                var document = parser.ParseDocument(html);

                // Grab the <script> elements that contains "__next_f.push"
                var scripts = document.Scripts
                                     .Where(s => s.TextContent.Contains("audio_url"));
                string? rawJson = null;

                foreach (var script in scripts) {
                    var arrayContent = GetSecondColumnFromScript(script.Text);

                    if (!string.IsNullOrWhiteSpace(arrayContent))
                        rawJson = arrayContent;
                }

                if (rawJson == null) 
                    throw new Exception("Failed to find track metadata");

                // Parse as a JArray
                JObject? obj = null;

                try {
                    if (rawJson.TrimStart()[0] == '{') {
                        obj = JObject.Parse(rawJson);
                    } else {
                        var arr = JArray.Parse(rawJson);
                        
                        foreach (var item in arr) {
                            if (item is JObject)
                                obj = (JObject)item;
                        }
                    }
                } catch { }

                if (obj == null)
                    throw new Exception("Failed to find track metadata");

                // Pull out the fields you want:
                string trackId = (string)obj["clip"]!["id"]!;
                string trackName = (string)obj["clip"]!["title"]!;

                string artistName = string.Empty;
                string artistImageUrl = string.Empty;

                if (obj["persona"] == null) {
                    artistName = (string)obj["persona"]!["user_display_name"]!;
                    artistImageUrl = (string)obj["persona"]!["user_image_url"]!;
                } else {
                    artistName = obj["clip"]!["display_name"]!.ToString().Trim();
                    artistImageUrl = obj["clip"]!["avatar_image_url"]!.ToString().Trim();
                }

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
            } catch (Exception ex) {
                return null;
            }
        }

        /// <summary>
        /// Returns the part after the first colon inside the first quoted literal.
        /// </summary>
        public static string? GetSecondColumnFromScript(string line) {
            var lines = Regex.Split(line, @"(?<!\\)\\n");
            
            if (lines.Length >= 3) {
                // Join the first and last
                line = lines[0] + lines[lines.Length-1];
            }

            //line = line.Split("\n")[0].Split("\\n"); // Get the first line

            // Find the first quote
            int firstQuoteIndex = line.IndexOf('\"');
            int firstColonIndex = line.IndexOf(':', firstQuoteIndex);
            int firstCommaIndex = line.IndexOf(',', firstQuoteIndex);

            int start = -1;

            if (firstColonIndex - firstQuoteIndex < 4) {
                start = firstColonIndex + 1;
            }
            else if (firstCommaIndex - firstQuoteIndex < 4) {
                start = firstCommaIndex + 1;
            }

            int end = line.LastIndexOf('\"');

            // Maybe the end is trailing a ] ???

            string response = line.Substring(start, (end - start));
            var unescape = Regex.Unescape(response);

            if (unescape[0] == '{') {
                // Check if the last character is a ], if so remove it
                if (unescape[unescape.Length-1] == ']') {
                    unescape = unescape.Substring(0, unescape.Length - 1);
                }
            }

            return unescape;
        }

    }
}
