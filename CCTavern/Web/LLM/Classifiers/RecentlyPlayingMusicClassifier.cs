using System;
using System.Collections.Generic;
using System.Text;

namespace CCTavern.Web.LLM.Classifiers {
    internal class RecentlyPlayingMusicClassifier {

        private static string BuildTagline(IReadOnlyList<string> themes) {
            string themeList = themes.Count switch {
                2 => $"{themes[0]} and {themes[1]}",
                3 => $"{themes[0]}, {themes[1]} and {themes[2]}",
                _ => throw new InvalidOperationException(
                    "Expected two or three themes.")
            };

            return $"Your room has been drifting between {themeList}.";
        }

        private static readonly string[] TaglineTemplates = [
            "Your room has been drifting between {0}.",
            "Your room has recently been mixing {0}.",
            "Recent listening has been moving between {0}."
        ];
    }
}