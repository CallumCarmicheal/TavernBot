using System;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CCTavern.Library {

    public static class FlexibleTimeSpanParser {
        private static readonly Regex WordRegex =
            new(@"(?<value>-?\d+(?:\.\d+)?)\s*(?<unit>days?|d|hours?|hrs?|hr|h|minutes?|mins?|mn|m|seconds?|secs?|s)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parse a duration expressed in "human" or "computer" notation.
        /// Throws <see cref="FormatException"/> when the string cannot be parsed.
        /// </summary>
        public static TimeSpan Parse(string input)
            => TryParse(input, out var ts)
               ? ts
               : throw new FormatException($"Could not parse \"{input}\" as a TimeSpan.");

        /// <summary>
        /// Safe version—returns false instead of throwing.
        /// </summary>
        public static bool TryParse(string input, out TimeSpan result) {
            result = default!;
            if (string.IsNullOrWhiteSpace(input)) return false;
            input = input.Trim();

            // 1. Standard .NET formats (includes invariant "c", "g", etc.).
            if (TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out result))
                return true;

            // 2. Explicit exact patterns to handle 5:00 as mm:ss (not hh:mm).
            string[] exact =
            {
            @"mm\:ss",          // 05:32  -> 0h 5m 32s
            @"hh\:mm\:ss",      // 01:30:05
            @"d\:hh\:mm\:ss"    // 1:02:03:04  -> 1d 2h 3m 4s
        };
            if (TimeSpan.TryParseExact(input, exact, CultureInfo.InvariantCulture,
                                       TimeSpanStyles.None, out result))
                return true;

            // 3. Colon-split heuristic (handles "30:02:10" without unit labels).
            if (ParseColonForm(input, out result))
                return true;

            // 4. Word/unit tokens ("1hr32m1s", "90 seconds", "1 mn").
            if (ParseWordForm(input, out result))
                return true;

            return false;
        }

        private static bool ParseColonForm(string input, out TimeSpan ts) {
            ts = default;
            var parts = input.Split(':');
            if (parts.Any(p => p.Length == 0 || p.Any(c => !char.IsDigit(c)))) return false;

            int[] nums = parts.Select(int.Parse).ToArray();
            switch (nums.Length) {
            case 2: ts = new TimeSpan(0, nums[0], nums[1]); return true;           // mm:ss
            case 3: ts = new TimeSpan(nums[0], nums[1], nums[2]); return true;     // hh:mm:ss
            case 4: ts = new TimeSpan(nums[0], nums[1], nums[2], nums[3]); return true; // d:hh:mm:ss
            default: return false;
            }
        }

        private static bool ParseWordForm(string input, out TimeSpan ts) {
            ts = default;
            var matches = WordRegex.Matches(input);
            if (matches.Count == 0) return false;

            double days = 0, hours = 0, minutes = 0, seconds = 0;

            foreach (Match m in matches) {
                var value = double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture);
                var unit = m.Groups["unit"].Value.ToLowerInvariant();

                if (unit.StartsWith('d')) days += value;
                else if (unit.StartsWith('h')) hours += value;
                else if (unit.StartsWith('m')) minutes += value;
                else seconds += value;
            }

            ts = new TimeSpan((int)days, 0, 0, 0)          // days
                 + TimeSpan.FromHours(hours)
                 + TimeSpan.FromMinutes(minutes)
                 + TimeSpan.FromSeconds(seconds);
            return true;
        }
    }

}
