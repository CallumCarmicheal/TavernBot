using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TimeSpanParserUtil;

namespace CCTavern {
    internal static class Extensions {
        //used by LINQ to SQL
        public static IQueryable<TSource> Page<TSource>(this IQueryable<TSource> source, int page, int pageSize) {
            return source.Skip((page - 1) * pageSize).Take(pageSize);
        }

        //used by LINQ
        public static IEnumerable<TSource> Page<TSource>(this IEnumerable<TSource> source, int page, int pageSize) {
            return source.Skip((page - 1) * pageSize).Take(pageSize);
        }

        public static IEnumerable<string> SplitWithTrim(this string text, char separator, char escapeCharacter, bool removeEmptyEntries) {
            string buffer = string.Empty;
            bool escape = false;

            foreach (var c in text) {
                if (!escape && c == separator) {
                    if (!removeEmptyEntries || buffer.Length > 0) {
                        yield return buffer;
                    }

                    buffer = string.Empty;
                } else {
                    if (c == escapeCharacter) {
                        escape = !escape;

                        if (!escape) {
                            buffer = string.Concat(buffer, c);
                        }
                    } else {
                        if (!escape) {
                            buffer = string.Concat(buffer, c);
                        }

                        escape = false;
                    }
                }
            }

            if (buffer.Length != 0) {
                yield return buffer.Trim();
            }
        }

        public static ulong ULongNext(this Random rand, ulong min, ulong max) {
            return (ulong)rand.LongNext( (long)min, (long)max );
        }

        public static long LongNext(this Random rand, long min, long max) {
            long result = rand.Next((Int32)(min >> 32), (Int32)(max >> 32));
            result = (result << 32);
            result = result | (long)rand.Next((Int32)min, (Int32)max);
            return result;
        }

        public static string ToDynamicTimestamp(this TimeSpan time, bool alwaysShowMinutes = false) {
            Stack<string> timestamp = new Stack<string>(4);

            if (time.Days > 0) timestamp.Push($"{time.Days:0}d {time.Hours:0}h {time.Minutes:00}m {time.Seconds:00}s");
            else if (time.Hours > 0) timestamp.Push($"{time.Hours:0}h {time.Minutes:00}m {time.Seconds:00}s");
            else if (time.Minutes > 0) timestamp.Push($"{time.Minutes:00}m {time.Seconds:00}s");
            else //if (time.Seconds > 0) 
                timestamp.Push((alwaysShowMinutes ? "00m " : "") + $"{time.Seconds:00}s");

            return string.Join(":", timestamp.Reverse());
        }

        public static TimeSpan? TryParseTimeStamp(this string input) {
            TimeSpan ts;

            if (TimeSpan.TryParseExact(input, new string[] { "ss", "mm\\:ss", "mm\\-ss", "mm\\'ss", "mm\\;ss" }, null, out ts))
                return ts;

            if (TimeSpanParser.TryParse(input, timeSpan: out ts))
                return ts;

            return null;
        }

        public static T? GetNearestByItemTimeSpan<T>(this SortedList<TimeSpan, T> thisList, TimeSpan thisValue)
        {
            var keys = thisList.Keys;
            var _where = keys.Where(k => k <= thisValue);

            if (_where.Any() == false)
                return default;

            var nearest = thisValue -
                _where.Min(k => thisValue - k);
            return thisList[nearest];
        }

        public static (T? item, TimeSpan startTime, TimeSpan? endTime) GetNearestByItemTimeSpanWithTimespanRegion<T>(this SortedList<TimeSpan, T> thisList, TimeSpan thisValue) {
            var keys = thisList.Keys;
            var _where = keys.Where(k => k <= thisValue);

            if (_where.Any() == false)
                return default;

            var nearest = thisValue -
                _where.Min(k => thisValue - k);

            var value = thisList[nearest];
            var idx = thisList.IndexOfKey(nearest);
            TimeSpan? endTime = null;

            try { endTime = thisList.GetKeyAtIndex(idx + 1); } 
            catch { }

            return (value, nearest, endTime);
        }


        ///<summary>Finds the index of the first item matching an expression in an enumerable.</summary>
        ///<param name="items">The enumerable to search.</param>
        ///<param name="predicate">The expression to test the items against.</param>
        ///<returns>The index of the first matching item, or -1 if no items match.</returns>
        public static int FindIndex<T>(this IEnumerable<T> items, Func<T, bool> predicate) {
            if (items == null) throw new ArgumentNullException("items");
            if (predicate == null) throw new ArgumentNullException("predicate");

            int retVal = 0;
            foreach (var item in items) {
                if (predicate(item)) return retVal;
                retVal++;
            }
            return -1;
        }
        ///<summary>Finds the index of the first occurrence of an item in an enumerable.</summary>
        ///<param name="items">The enumerable to search.</param>
        ///<param name="item">The item to find.</param>
        ///<returns>The index of the first matching item, or -1 if the item was not found.</returns>
        public static int IndexOf<T>(this IEnumerable<T> items, T item) { return items.FindIndex(i => EqualityComparer<T>.Default.Equals(item, i)); }
    }
}
