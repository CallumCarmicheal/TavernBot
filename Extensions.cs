using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
