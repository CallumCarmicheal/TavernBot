using CCTavern.Database;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    public class QueueSearchModule : BaseCommandModule {
        public ILogger<QueueSearchModule> logger { get; private set; }

        const int ITEMS_PER_PAGE = 10;

        public QueueSearchModule(ILogger<QueueSearchModule> logger) {
            this.logger = logger;
        }


        [Command("query"), Aliases("qry", "searchQueue", "sq")]
        [Description("Search queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task SearchQueue(CommandContext ctx,
            [RemainingText] string searchString
        ) {
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            IQueryable<GuildQueueItem> baseQuery = db.GuildQueueItems
                .Include(p => p.RequestedBy)
                .Include(p => p.Playlist)
                .Where(qi => qi.GuildId == guild.Id && qi.IsDeleted == false);

            // ===== Flags / defaults
            bool showPlan = false;
            bool showDate = false;
            bool showTime = false;
            bool caseSensitive = false;
            var orderSpecs = new List<(string key, bool desc)>();
            string? targetPageString = "1";
            searchString ??= string.Empty;

            if (Regex.IsMatch(searchString, @"(?i)(^|\s)--help(\s|$)")) {
                await ctx.RespondAsync($"```txt\n{BuildHelpText()}\n```");
                return;
            }

            ConsumeSimpleFlag("--date", () => showDate = true);
            ConsumeSimpleFlag("--time", () => showTime = true);
            ConsumeSimpleFlagWithAliases(["--plan", "--verbose", "--v"], () => showPlan = true);
            ConsumeSimpleFlagWithAliases(["--case-sens", "--casesens"], () => caseSensitive = true);

            // --page (support numbers + keywords: max/last/latest/auto)
            {
                // accept +1, -1, 3  OR  max|last|latest|auto
                var rxPage = new Regex(@"(?is)(^|\s)--page(?:\s*[:=]\s*|\s+)(?<v>([+\-]?\d+)|max|last|latest|auto)(?=\s|$)");
                var m = rxPage.Matches(searchString).Cast<Match>().LastOrDefault();
                if (m != null && m.Success) {
                    targetPageString = m.Groups["v"].Value.Trim();
                    searchString = rxPage.Replace(searchString, " ");
                }
            }

            // --orderby (multi)
            {
                var rxOrder = new Regex(@"(?is)(^|\s)--orderby(?:=|\s+)(?<spec>.*?)(?=(\s--\w+)|$)");
                foreach (Match m in rxOrder.Matches(searchString)) {
                    var spec = m.Groups["spec"].Value.Trim();
                    foreach (var piece in spec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
                        var p = piece.Trim();
                        if (string.IsNullOrEmpty(p)) continue;
                        var parts = p.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var key = parts.Length >= 1 ? parts[0] : null;
                        var dir = parts.FirstOrDefault(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
                                                            x.Equals("desc", StringComparison.OrdinalIgnoreCase));
                        if (key != null && IsValidOrderKey(key)) {
                            bool desc = dir?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
                            orderSpecs.Add((key, desc));
                        }
                    }
                }
                searchString = rxOrder.Replace(searchString, " ");
            }

            // Remove any remaining flags.
            searchString = Regex.Replace(
                searchString,
                @"(^|\s)--[A-Za-z][\w\-]*(?:\s*[:=]\s*(""[^""]*""|\S+)|\s+(""[^""]*""|\S+))?",
                " ",
                RegexOptions.CultureInvariant
            );

            // ===== Build plan header
            var plan = new StringBuilder();
            plan.AppendLine("Query Plan:");
            plan.AppendLine($"- Flags: date={showDate}, time={showTime}, case-sens={caseSensitive}, page={(targetPageString ?? "<first>")}");
            if (orderSpecs.Count == 0) plan.AppendLine("- OrderBy: pos ASC (default)");
            else plan.AppendLine($"- OrderBy: {string.Join(", ", orderSpecs.Select(os => $"{CanonicalOrderKey(os.key)} {(os.desc ? "DESC" : "ASC")}"))}");

            // ===== Parse into a boolean expression with parentheses + OR
            // Top-level OR split -> alternatives. Each alternative is AND of segments;
            // a segment is either plain tokens (AND) or a parenthesized OR of token-lists.
            var alternatives = SplitTopLevelOrs(searchString)
                .Select(ParseAlternativeIntoSegments)
                .ToList();

            // ===== Build predicates and the plan text
            ParameterExpression gParam = Expression.Parameter(typeof(GuildQueueItem), "g");
            Expression? finalPredicate = null;
            int altIdx = 0;

            foreach (var alt in alternatives) {
                altIdx++;
                // Build predicate for this alternative:
                Expression altPred = True();

                var altPlan = new StringBuilder();
                altPlan.AppendLine($"- Group {altIdx} (AND):");

                foreach (var seg in alt) {
                    if (!seg.IsChoice) {
                        // Plain tokens ANDed
                        foreach (var t in seg.Tokens) {
                            var expr = BuildTokenPredicate(t, gParam, db, guild, caseSensitive, altPlan);
                            if (expr != null) altPred = And(altPred, expr);
                        }
                    } else {
                        // Parenthesized OR of choices; each choice is a list of tokens ANDed
                        Expression orExpr = False();
                        var choiceLines = new List<string>();
                        foreach (var choice in seg.Choices) {
                            Expression choicePred = True();
                            var parts = new List<string>();
                            foreach (var t in choice) {
                                var descOnly = DescribeToken(t, caseSensitive);
                                parts.Add(descOnly);
                                var expr = BuildTokenPredicate(t, gParam, db, guild, caseSensitive, null);
                                if (expr != null) choicePred = And(choicePred, expr);
                            }
                            orExpr = Or(orExpr, choicePred);
                            choiceLines.Add(string.Join(" AND ", parts));
                        }
                        altPred = And(altPred, orExpr);
                        altPlan.AppendLine($"  • ( {string.Join("  OR  ", choiceLines)} )");
                    }
                }

                plan.Append(altPlan);
                finalPredicate = finalPredicate == null ? altPred : Or(finalPredicate, altPred);
            }

            IQueryable<GuildQueueItem> guildQueueQuery = baseQuery;
            if (finalPredicate != null) {
                var lambda = Expression.Lambda<Func<GuildQueueItem, bool>>(finalPredicate, gParam);
                guildQueueQuery = guildQueueQuery.Where(lambda);
            }

            // ===== ORDER BY (default pos ASC)
            if (orderSpecs.Count == 0) {
                guildQueueQuery = guildQueueQuery.OrderBy(x => x.Position);
            } else {
                IOrderedQueryable<GuildQueueItem>? ordered = null;
                foreach (var (key, desc) in orderSpecs) {
                    switch (CanonicalOrderKey(key)) {
                    case "pos": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.Position, desc); break;
                    case "title": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.Title, desc); break;
                    case "len": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.Length, desc); break;
                    case "created": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.CreatedAt, desc); break;
                    case "pid": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.PlaylistId, desc); break;
                    case "playlist": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.Playlist != null ? x.Playlist.Title : null, desc); break;
                    case "playlistsongs": ordered = ApplyOrder(ordered, guildQueueQuery, x => x.Playlist != null ? x.Playlist.PlaylistSongCount : 0, desc); break;
                    }
                    guildQueueQuery = ordered ?? guildQueueQuery;
                }
            }

            // ===== Log SQL + plan; show plan optionally in response
            try {
                var sql = guildQueueQuery.ToQueryString();
                logger.LogDebug("{Plan}\n\nEF SQL:\n{SQL}", plan.ToString(), sql);
            } catch {
                logger.LogDebug("{Plan}", plan.ToString());
            }

            await PrintQueueResponse(ctx, guildQueueQuery, targetPageString!, showDate, showTime,
                verbosePlan: showPlan ? plan.ToString() : null);

            // ================== Helpers ==================

            // ----- Flags
            void ConsumeSimpleFlag(string flag, Action set) {
                var rx = new Regex($@"(?i)(^|\s){Regex.Escape(flag)}(\s|$)");
                if (rx.IsMatch(searchString)) {
                    set();
                    searchString = rx.Replace(searchString, " ");
                }
            }
            void ConsumeSimpleFlagWithAliases(string[] flags, Action set) {
                foreach (var flag in flags) {
                    var rx = new Regex($@"(?i)(^|\s){Regex.Escape(flag)}(\s|$)");
                    if (rx.IsMatch(searchString)) {
                        set();
                        searchString = rx.Replace(searchString, " ");
                    }
                }
            }
        }

    #region Parsing model / Tokens
        record Token(string? Key, string Op, string Value, bool Negate);

        sealed class Segment {
            public bool IsChoice;                                 // false = plain AND tokens; true = parenthesized OR choices
            public List<Token> Tokens = new();                    // when IsChoice == false
            public List<List<Token>> Choices = new();             // when IsChoice == true; each choice is a list of tokens ANDed
        }

        /// <summary>
        /// Top-level OR splitter that respects parentheses
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static List<string> SplitTopLevelOrs(string s) {
            var parts = new List<string>();
            int depth = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length;) {
                char c = s[i];
                if (c == '(') { depth++; sb.Append(c); i++; continue; }
                if (c == ')') { depth = Math.Max(0, depth - 1); sb.Append(c); i++; continue; }
                if (depth == 0) {
                    // '|' at top level
                    if (c == '|') { parts.Add(sb.ToString().Trim()); sb.Clear(); i++; continue; }
                    // 'OR' at top level with word boundaries
                    if ((c == 'O' || c == 'o') && i + 1 < s.Length && (s[i + 1] == 'R' || s[i + 1] == 'r')) {
                        // check boundaries
                        bool leftOk = i == 0 || char.IsWhiteSpace(s[i - 1]);
                        bool rightOk = (i + 2 == s.Length) || char.IsWhiteSpace(s[i + 2]);
                        if (leftOk && rightOk) { parts.Add(sb.ToString().Trim()); sb.Clear(); i += 2; continue; }
                    }
                }
                sb.Append(c); i++;
            }
            var tail = sb.ToString().Trim();
            if (tail.Length > 0) parts.Add(tail);
            if (parts.Count == 0) parts.Add(""); // empty -> single alternative
            return parts;
        }

        /// <summary>
        /// Parse an alternative into segments (plain tokens ANDed, or parenthesized OR choices)
        /// </summary>
        /// <param name="alt"></param>
        /// <returns></returns>
        static List<Segment> ParseAlternativeIntoSegments(string alt) {
            var segs = new List<Segment>();
            var sb = new StringBuilder();
            for (int i = 0; i < alt.Length; i++) {
                if (alt[i] == '(') {
                    // flush plain text before '('
                    var before = sb.ToString().Trim();
                    if (before.Length > 0) {
                        var plain = new Segment { IsChoice = false };
                        plain.Tokens.AddRange(Tokenize(before));
                        segs.Add(plain);
                        sb.Clear();
                    }
                    // find matching ')'
                    int depth = 1; int j = i + 1;
                    for (; j < alt.Length && depth > 0; j++) {
                        if (alt[j] == '(') depth++;
                        else if (alt[j] == ')') depth--;
                    }
                    var inside = alt.Substring(i + 1, j - i - 2); // between ( and )
                    var choices = SplitTopLevelOrs(inside);
                    var seg = new Segment { IsChoice = true };
                    foreach (var choice in choices) {
                        var tokens = Tokenize(choice);
                        if (tokens.Count > 0) seg.Choices.Add(tokens);
                    }
                    segs.Add(seg);
                    i = j - 1; // jump to after ')'
                } else {
                    sb.Append(alt[i]);
                }
            }
            var rest = sb.ToString().Trim();
            if (rest.Length > 0) {
                var plain = new Segment { IsChoice = false };
                plain.Tokens.AddRange(Tokenize(rest));
                segs.Add(plain);
            }
            if (segs.Count == 0) segs.Add(new Segment { IsChoice = false });
            return segs;
        }

        /// <summary>
        /// Tokenizer: comparisons or bare words; optional leading '-' (negation)
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static List<Token> Tokenize(string s) {
            var tokens = new List<Token>();
            // Add spacing around parentheses just in case
            s = s.Replace("(", " ( ").Replace(")", " ) ").Trim();
            var rx = new Regex(
                @"(?<neg>-)?(?<key>[A-Za-z][A-Za-z0-9]*)\s*(?<op>:|=|!=|>=|<=|>|<)\s*(?<val>""[^""]*""|\S+)|(?<neg2>-)?(?<bare>""[^""]*""|\S+)",
                RegexOptions.CultureInvariant);
            static string Unq(string t) => (t.Length >= 2 && t.StartsWith("\"") && t.EndsWith("\"")) ? t[1..^1] : t;

            var m = rx.Matches(s);
            foreach (Match mm in m) {
                if (mm.Groups["key"].Success) {
                    var key = mm.Groups["key"].Value;
                    var op = mm.Groups["op"].Value;
                    var val = Unq(mm.Groups["val"].Value);
                    bool neg = mm.Groups["neg"].Success;
                    // skip OR tokens that may slip through (unlikely here)
                    if (string.Equals(key, "or", StringComparison.OrdinalIgnoreCase)) continue;
                    tokens.Add(new Token(key, op, val, neg));
                } else if (mm.Groups["bare"].Success) {
                    var raw = Unq(mm.Groups["bare"].Value);
                    if (string.Equals(raw, "OR", StringComparison.OrdinalIgnoreCase) || raw == "|") continue;
                    bool neg = mm.Groups["neg2"].Success;
                    tokens.Add(new Token(null, ":", raw, neg)); // bare -> title contains
                }
            }
            return tokens;
        }

        /// <summary>
        /// Build predicate & plan text for a token
        /// </summary>
        /// <param name="t">Token</param>
        /// <param name="g">Parameter expression</param>
        /// <param name="db">Database context</param>
        /// <param name="guild">Discord guild</param>
        /// <param name="caseSensitive">If expression is case sensitive</param>
        /// <param name="planOut">Plan</param>
        /// <returns></returns>
        static Expression? BuildTokenPredicate(Token t, ParameterExpression g,
            TavernContext db, Guild guild, bool caseSensitive, StringBuilder? planOut) {
            string k = NormalizeKey(t.Key ?? "title");

            // For description
            if (planOut != null) {
                planOut.AppendLine("  • " + DescribeToken(t, caseSensitive));
            }

            // Helpers to create property expressions
            static Expression F(Expression obj, string name) => Expression.PropertyOrField(obj, name);

            // String helpers
            static Expression StrContains(Expression str, string value, bool caseSensitive) {
                var nullConst = Expression.Constant(null, typeof(string));
                var notNull = Expression.NotEqual(str, nullConst);
                Expression left = str;
                if (!caseSensitive) {
                    var toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
                    left = Expression.Call(str, toLower);
                    value = value.ToLowerInvariant();
                }
                var contains = Expression.Call(left, typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!, Expression.Constant(value));
                return Expression.AndAlso(notNull, contains);
            }
            static Expression StrEquals(Expression str, string value, bool caseSensitive) {
                var nullConst = Expression.Constant(null, typeof(string));
                var notNull = Expression.NotEqual(str, nullConst);
                Expression left = str, right = Expression.Constant(value);
                if (!caseSensitive) {
                    var toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
                    left = Expression.Call(str, toLower);
                    right = Expression.Call(right, toLower);
                }
                return Expression.AndAlso(notNull, Expression.Equal(left, right));
            }

            // Comparable helpers
            static Expression Cmp(Expression left, string op, Expression right) => op switch {
                "=" => Expression.Equal(left, right),
                "!=" => Expression.NotEqual(left, right),
                ">" => Expression.GreaterThan(left, right),
                ">=" => Expression.GreaterThanOrEqual(left, right),
                "<" => Expression.LessThan(left, right),
                "<=" => Expression.LessThanOrEqual(left, right),
                ":" => Expression.Equal(left, right), // ':' treated as equality for non-strings
                _ => Expression.Equal(Expression.Constant(1), Expression.Constant(1)) // true
            };

            // Build by key
            switch (k) {
            case "title": {
                    var prop = F(g, "Title");
                    if (t.Op == "=") {
                        var eq = StrEquals(prop, t.Value, caseSensitive);
                        return t.Negate ? Expression.Not(eq) : eq;
                    } else if (t.Op == "!=") {
                        return Expression.Not(StrContains(prop, t.Value, caseSensitive));
                    } else { // ':'
                        var c = StrContains(prop, t.Value, caseSensitive);
                        return t.Negate ? Expression.Not(c) : c;
                    }
                }
            case "playlist": {
                    var pl = F(g, "Playlist");
                    var title = F(pl, "Title");
                    var notNull = Expression.NotEqual(pl, Expression.Constant(null, typeof(GuildQueuePlaylist)));
                    Expression inner;
                    if (t.Op == "=") {
                        inner = StrEquals(title, t.Value, caseSensitive);
                    } else if (t.Op == "!=") {
                        inner = Expression.Not(StrContains(title, t.Value, caseSensitive));
                    } else {
                        inner = StrContains(title, t.Value, caseSensitive);
                    }
                    return t.Negate ? Expression.Not(Expression.AndAlso(notNull, inner))
                                    : Expression.AndAlso(notNull, inner);
                }
            case "user": {
                    // user:<@123> => RequestedBy.UserId == 123  (negate => !=)
                    if (TryParseMentionId(t.Value, out var uid)) {
                        var rb = F(g, "RequestedBy");
                        var notNull = Expression.NotEqual(rb, Expression.Constant(null, typeof(CachedUser)));
                        var userId = F(rb, "UserId");
                        var rhs = Expression.Constant(uid);
                        var cmp = t.Negate || t.Op == "!=" ? Expression.NotEqual(userId, rhs)
                                                           : Expression.Equal(userId, rhs);
                        return Expression.AndAlso(notNull, cmp);
                    } else {
                        var rb = F(g, "RequestedBy");
                        var notNull = Expression.NotEqual(rb, Expression.Constant(null, typeof(CachedUser)));
                        var un = F(rb, "Username");
                        var dn = F(rb, "DisplayName");

                        Expression clause;
                        if (t.Op == "=") {
                            clause = Expression.OrElse(StrEquals(un, t.Value, caseSensitive), StrEquals(dn, t.Value, caseSensitive));
                            if (t.Negate) clause = Expression.Not(clause);
                        } else if (t.Op == "!=") {
                            clause = Expression.Not(Expression.OrElse(StrContains(un, t.Value, caseSensitive), StrContains(dn, t.Value, caseSensitive)));
                        } else { // contains
                            clause = Expression.OrElse(StrContains(un, t.Value, caseSensitive), StrContains(dn, t.Value, caseSensitive));
                            if (t.Negate) clause = Expression.Not(clause);
                        }
                        return Expression.AndAlso(notNull, clause);
                    }
                }
            case "pos": {
                    var left = F(g, "Position"); // ulong
                    if ((t.Op == ":" || t.Op == "=") && t.Value.Contains("..")) {
                        if (TryParseRangeULong(t.Value, out var a, out var b)) {
                            var ge = Cmp(left, ">=", Expression.Constant(a));
                            var le = Cmp(left, "<=", Expression.Constant(b));
                            var between = Expression.AndAlso(ge, le);
                            if (t.Negate) {
                                var lt = Cmp(left, "<", Expression.Constant(a));
                                var gt = Cmp(left, ">", Expression.Constant(b));
                                return Expression.OrElse(lt, gt);
                            }
                            return between;
                        }
                        return null;
                    }
                    if (ulong.TryParse(t.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var posv)) {
                        var cmp = Cmp(left, t.Op, Expression.Constant(posv));
                        return t.Negate ? Expression.Not(cmp) : cmp;
                    }
                    return null;
                }
            case "len": {
                    var left = F(g, "Length"); // TimeSpan
                    if ((t.Op == ":" || t.Op == "=") && t.Value.Contains("..")) {
                        if (TryParseDuration(t.Value.Split("..")[0], out var a) &&
                            TryParseDuration(t.Value.Split("..")[1], out var b)) {
                            var ge = Cmp(left, ">=", Expression.Constant(a));
                            var le = Cmp(left, "<=", Expression.Constant(b));
                            var between = Expression.AndAlso(ge, le);
                            if (t.Negate) {
                                var lt = Cmp(left, "<", Expression.Constant(a));
                                var gt = Cmp(left, ">", Expression.Constant(b));
                                return Expression.OrElse(lt, gt);
                            }
                            return between;
                        }
                        return null;
                    }
                    if (TryParseDuration(t.Value, out var ts)) {
                        var cmp = Cmp(left, t.Op, Expression.Constant(ts));
                        return t.Negate ? Expression.Not(cmp) : cmp;
                    }
                    return null;
                }
            case "created": {
                    var left = F(g, "CreatedAt"); // DateTime
                    if ((t.Op == ":" || t.Op == "=") && t.Value.Contains("..")) {
                        if (TryParseDateish(t.Value.Split("..")[0], out var a) &&
                            TryParseDateish(t.Value.Split("..")[1], out var b)) {
                            var ge = Cmp(left, ">=", Expression.Constant(a));
                            var le = Cmp(left, "<=", Expression.Constant(b));
                            var between = Expression.AndAlso(ge, le);
                            if (t.Negate) {
                                var lt = Cmp(left, "<", Expression.Constant(a));
                                var gt = Cmp(left, ">", Expression.Constant(b));
                                return Expression.OrElse(lt, gt);
                            }
                            return between;
                        }
                        return null;
                    }
                    if (TryParseDateish(t.Value, out var dt)) {
                        var cmp = Cmp(left, t.Op, Expression.Constant(dt));
                        return t.Negate ? Expression.Not(cmp) : cmp;
                    }
                    return null;
                }
            case "pid": {
                    var left = F(g, "PlaylistId"); // ulong?
                    if ((t.Op == ":" || t.Op == "=") && t.Value.Contains("..")) {
                        if (TryParseRangeULong(t.Value, out var a, out var b)) {
                            // HasValue && between (or negation as outside-OR)
                            var has = Expression.Property(left, "HasValue");
                            var val = Expression.Property(left, "Value");
                            var ge = Cmp(val, ">=", Expression.Constant(a));
                            var le = Cmp(val, "<=", Expression.Constant(b));
                            var between = Expression.AndAlso(ge, le);
                            var inRange = Expression.AndAlso(has, between);
                            if (t.Negate) {
                                var lt = Cmp(val, "<", Expression.Constant(a));
                                var gt = Cmp(val, ">", Expression.Constant(b));
                                var outside = Expression.OrElse(lt, gt);
                                return Expression.AndAlso(has, outside);
                            }
                            return inRange;
                        }
                        return null;
                    }
                    if (ulong.TryParse(t.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) {
                        var has = Expression.Property(left, "HasValue");
                        var val = Expression.Property(left, "Value");
                        var cmp = Cmp(val, t.Op, Expression.Constant(id));
                        var pred = Expression.AndAlso(has, cmp);
                        return t.Negate ? Expression.Not(pred) : pred;
                    }
                    return null;
                }
            case "playlistsongs": {
                    // Playlist != null && Playlist.PlaylistSongCount <cmp> value
                    var pl = F(g, "Playlist");
                    var cnt = F(pl, "PlaylistSongCount");
                    var notNull = Expression.NotEqual(pl, Expression.Constant(null, typeof(GuildQueuePlaylist)));
                    if ((t.Op == ":" || t.Op == "=") && t.Value.Contains("..")) {
                        if (TryParseRangeInt(t.Value, out var a, out var b)) {
                            var ge = Cmp(cnt, ">=", Expression.Constant(a));
                            var le = Cmp(cnt, "<=", Expression.Constant(b));
                            var between = Expression.AndAlso(ge, le);
                            var pred = Expression.AndAlso(notNull, between);
                            if (t.Negate) {
                                var lt = Cmp(cnt, "<", Expression.Constant(a));
                                var gt = Cmp(cnt, ">", Expression.Constant(b));
                                var outside = Expression.AndAlso(notNull, Expression.OrElse(lt, gt));
                                return outside;
                            }
                            return pred;
                        }
                        return null;
                    }
                    if (int.TryParse(t.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pcount)) {
                        var cmp = Cmp(cnt, t.Op, Expression.Constant(pcount));
                        var pred = Expression.AndAlso(notNull, cmp);
                        return t.Negate ? Expression.Not(pred) : pred;
                    }
                    return null;
                }
            case "pcontains": {
                    // Find playlists that contain song Title matching value (contains; honor case-sens),
                    // then include all queue items from those playlists
                    var v = caseSensitive ? t.Value : t.Value.ToLowerInvariant();
                    var matchedPlaylistIds =
                        db.GuildQueueItems
                          .Where(x => x.GuildId == guild.Id && x.PlaylistId != null && x.Playlist != null
                                      && x.Playlist.Songs.Any(s =>
                                            s.Title != null &&
                                            (caseSensitive ? s.Title.Contains(t.Value)
                                                           : s.Title.ToLower().Contains(v))))
                          .Select(x => x.PlaylistId!.Value)
                          .Distinct();

                    var plid = Expression.PropertyOrField(g, "PlaylistId");
                    var has = Expression.Property(plid, "HasValue");
                    var val = Expression.Property(plid, "Value");
                    var containsCall = Expression.Call(
                        typeof(Queryable),
                        nameof(Queryable.Contains),
                        new[] { typeof(ulong) },
                        Expression.Constant(matchedPlaylistIds),
                        val
                    );
                    var pred = Expression.AndAlso(has, containsCall);
                    return t.Negate ? Expression.Not(pred) : pred;
                }
            }

            return null; // unknown key -> ignore
        }

        /// <summary>
        /// Describe token for plan text
        /// </summary>
        /// <param name="t"></param>
        /// <param name="caseSensitive"></param>
        /// <returns></returns>
        static string DescribeToken(Token t, bool caseSensitive) {
            string k = NormalizeKey(t.Key ?? "title");
            string cs = caseSensitive ? "(cs)" : "(ci)";
            string opText = t.Op switch {
                "=" => "EQUALS",
                "!=" => "NOT CONTAINS", // for strings; for numbers it's printed as operator below
                ":" => "CONTAINS",
                ">" => ">",
                ">=" => ">=",
                "<" => "<",
                "<=" => "<=",
                _ => t.Op
            };
            if (k is "pos" or "created" or "len" or "pid" or "playlistsongs") {
                if ((t.Op == ":" || t.Op == "=") && t.Value.Contains(".."))
                    return (t.Negate ? "-" : "") + $"{k} in [{t.Value}]";
                return (t.Negate ? "-" : "") + $"{k} {t.Op} {t.Value}";
            }
            if (k is "playlist" or "title") {
                if (t.Op == "=") return (t.Negate ? "NOT " : "") + $"{k} EQUALS \"{t.Value}\" {cs}";
                if (t.Op == "!=") return $"{k} NOT CONTAINS \"{t.Value}\" {cs}";
                return (t.Negate ? "NOT " : "") + $"{k} CONTAINS \"{t.Value}\" {cs}";
            }
            if (k == "user") {
                if (TryParseMentionId(t.Value, out _)) {
                    return (t.Negate || t.Op == "!=") ? $"user id != {t.Value}" : $"user id = {t.Value}";
                } else {
                    return t.Op switch {
                        "=" => (t.Negate ? "NOT " : "") + $"user EQUALS \"{t.Value}\" {cs} in Username/DisplayName",
                        "!=" => $"user NOT CONTAINS \"{t.Value}\" {cs} in Username/DisplayName",
                        _ => (t.Negate ? "NOT " : "") + $"user CONTAINS \"{t.Value}\" {cs} in Username/DisplayName"
                    };
                }
            }
            if (k == "pcontains") {
                return (t.Negate ? "NOT " : "") + $"pContains \"{t.Value}\" {cs} → expand playlists";
            }
            return (t.Negate ? "-" : "") + $"{k} {opText} \"{t.Value}\" {cs}";
        }

    #endregion

    #region Expression combinators
        static Expression True() => Expression.Equal(Expression.Constant(1), Expression.Constant(1));
        static Expression False() => Expression.Not(True());
        static Expression And(Expression a, Expression b) => Expression.AndAlso(a, b);
        static Expression Or(Expression a, Expression b) => Expression.OrElse(a, b);
    #endregion

    #region Key/ordering helpers
        static string NormalizeKey(string key) {
            var k = key.ToLowerInvariant();
            if (k is "text") k = "title";
            if (k is "position") k = "pos";
            if (k is "length") k = "len";
            if (k is "queued" or "added") k = "created";
            if (k is "playlistid") k = "pid";
            if (k is "ptitle" or "playlisttitle") k = "playlist";
            if (k is "playlistsongs" or "playlistcount" or "pcount" or "psongs") k = "playlistsongs";
            if (k is "playlistsong" or "playlistcontains") k = "pcontains";
            return k;
        }
        static string CanonicalOrderKey(string key) {
            var k = NormalizeKey(key);
            return k switch { "position" => "pos", "length" => "len", _ => k };
        }
        static bool IsValidOrderKey(string key) {
            key = NormalizeKey(key);
            return key is "pos" or "title" or "len" or "created" or "pid" or "playlist" or "playlistsongs";
        }
        static IOrderedQueryable<GuildQueueItem> ApplyOrder<T>(
            IOrderedQueryable<GuildQueueItem>? ordered,
            IQueryable<GuildQueueItem> source,
            Expression<Func<GuildQueueItem, T>> key,
            bool desc) {
            if (ordered == null) return desc ? source.OrderByDescending(key) : source.OrderBy(key);
            return desc ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
        }
    #endregion

    #region Parsing Utilities
        static bool TryParseMentionId(string input, out ulong id) {
            id = 0;
            var m = Regex.Match(input, @"^<@(?<id>\d+)>$");
            return m.Success && ulong.TryParse(m.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
        }
        static bool TryParseRangeULong(string s, out ulong a, out ulong b) {
            a = b = 0;
            var parts = s.Split(new[] { ".." }, StringSplitOptions.None);
            if (parts.Length != 2) return false;
            if (ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var aa) &&
                ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bb)) {
                if (aa > bb) (aa, bb) = (bb, aa);
                a = aa; b = bb; return true;
            }
            return false;
        }
        static bool TryParseRangeInt(string s, out int a, out int b) {
            a = b = 0;
            var parts = s.Split(new[] { ".." }, StringSplitOptions.None);
            if (parts.Length != 2) return false;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var aa) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bb)) {
                if (aa > bb) (aa, bb) = (bb, aa);
                a = aa; b = bb; return true;
            }
            return false;
        }
        static bool TryParseDuration(string input, out TimeSpan span) {
            span = default;
            if (string.IsNullOrWhiteSpace(input)) return false;
            input = input.Trim().ToLowerInvariant();
            if (input.EndsWith("s", StringComparison.Ordinal) &&
                ulong.TryParse(input[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var s)) { span = TimeSpan.FromSeconds((long)s); return true; }
            if (input.EndsWith("m", StringComparison.Ordinal) &&
                ulong.TryParse(input[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)) { span = TimeSpan.FromMinutes((long)m); return true; }
            if (ulong.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) { span = TimeSpan.FromSeconds((long)seconds); return true; }
            var parts = input.Split(':');
            if (parts.Length is 2 or 3 &&
                int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ss) &&
                int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mm)) {
                int hh = 0;
                if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hh))
                    return false;
                span = new TimeSpan(hh, mm, ss);
                return true;
            }
            return TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out span);
        }
        static bool TryParseDateish(string input, out DateTime dt) {
            dt = default;
            if (string.IsNullOrWhiteSpace(input)) return false;
            var s = input.Trim().ToLowerInvariant();
            if (s == "today") { dt = DateTime.UtcNow.Date; return true; }
            if (s == "yesterday") { dt = DateTime.UtcNow.Date.AddDays(-1); return true; }
            var rm = Regex.Match(s, @"^-(?<n>\d+)(?<u>[dh])$");
            if (rm.Success) {
                var n = int.Parse(rm.Groups["n"].Value, CultureInfo.InvariantCulture);
                dt = rm.Groups["u"].Value == "d" ? DateTime.UtcNow.AddDays(-n) : DateTime.UtcNow.AddHours(-n);
                return true;
            }
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                return true;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt);
        }
    #endregion

        /// <summary>
        /// Build the help text
        /// </summary>
        /// <returns></returns>
        static string BuildHelpText() {
            var sb = new StringBuilder();
            sb.AppendLine("Search Queue — Help");
            sb.AppendLine();
            sb.AppendLine("Syntax: key op value   (space = AND)    OR / | for OR");
            sb.AppendLine("Strings: '=' means EQUALS, ':' means CONTAINS (case-insensitive by default)");
            sb.AppendLine("Numbers/Dates: = != > >= < <=   Ranges: key:from..to");
            sb.AppendLine();
            sb.AppendLine("Fields (aliases):");
            sb.AppendLine("  title|text");
            sb.AppendLine("  pos|position");
            sb.AppendLine("  len|length");
            sb.AppendLine("  created|queued|added");
            sb.AppendLine("  pid|playlistId");
            sb.AppendLine("  playlist|pTitle|playlistTitle");
            sb.AppendLine("  playlistSongs|playlistCount|pCount|pSongs");
            sb.AppendLine("  pContains|playlistSong|playlistContains  (expands to all songs in matched playlists)");
            sb.AppendLine("  user:@Name  or  user:partOfName  or  user=ExactName");
            sb.AppendLine();
            sb.AppendLine("Flags:");
            sb.AppendLine("  --orderby <spec>     e.g. --orderby pos:asc,length:desc  (multi)");
            sb.AppendLine("  --page +1 / --page -1 / --page 3 / --page (max, last, latest, auto)");
            sb.AppendLine("  --date   --time      (formatting)");
            sb.AppendLine("  --case-sens          (case-sensitive strings)");
            sb.AppendLine("  --v / --verbose / --plan");
            sb.AppendLine("  --help");
            sb.AppendLine();
            sb.AppendLine("Examples:");
            sb.AppendLine("  title:\"ocean waves\" len<=3:30 --orderby pos:asc");
            sb.AppendLine("  (title:lofi OR title:chill) playlist:\"music\" --date --time");
            sb.AppendLine("  pTitle:\"study mix\" pContains:lofi --orderby created:desc");
            sb.AppendLine("  user:@User created:-7d --page:+1");
            sb.AppendLine("  user:keyword created:-7d --page:+1");
            return sb.ToString();
        }


        /// <summary>
        /// Takes in the guildQueueQuery and prints a response to the user
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="guildQueueQuery"></param>
        /// <param name="targetPageString">The target page.</param>
        /// <param name="showDate">Show the date</param>
        /// <param name="showTime">Show the timestamp</param>
        /// <param name="verbosePlan">Show the plan in the message</param>
        /// <returns></returns>
        public async Task PrintQueueResponse(CommandContext ctx,
            /// Filtered query. 
            IQueryable<GuildQueueItem> guildQueueQuery,
            /// Page number to view, if blank then the page containing the current track is shown. (Supports relative +1, -12)
            string targetPageString = null!,
            /// Show date
            bool showDate = false,
            /// Show time
            bool showTime = false,
            /// Verbose Plan
            string? verbosePlan = null
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            if (verbosePlan != null)
                queueContent += verbosePlan! + "\n";

            // Get the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            // Get my own version of the GuildQueueItems just to get the current track index.
            ulong currentPosition = guild.CurrentTrack;

            var targetPage = 1;// (int)Math.Ceiling((decimal)currentPosition / ITEMS_PER_PAGE);

            // Parse user's target page
            if (targetPageString != null) {
                targetPageString = targetPageString.Trim();
                int output;

                try {
                    if (targetPageString.StartsWith("+")) {
                        string strToConvert = targetPageString[1..];
                        if (int.TryParse(strToConvert, out output)) {
                            targetPage += output;
                        }
                    } else if (targetPageString.StartsWith("-")) {
                        string strToConvert = targetPageString[1..];
                        if (int.TryParse(strToConvert, out output)) {
                            targetPage -= output;
                        }
                    } else if (int.TryParse(targetPageString, out output)) {
                        targetPage = output;
                    }
                } catch { }
            }

            if (targetPage < 1)
                targetPage = 1;

            var guildQueueCount = await guildQueueQuery.CountAsync();
            var pages = (int)Math.Ceiling(guildQueueCount / (double)ITEMS_PER_PAGE);

            // If we are getting the max page, just check for max.
            if ((targetPageString?.Contains("max") ?? false)
                || (targetPageString?.Contains("latest") ?? false)
                || (targetPageString?.Contains("last") ?? false)
                || (targetPageString?.Contains("auto") ?? false))
                targetPage = pages;

            targetPage = pages == 0 ? 0 : Math.Clamp(targetPage, 1, pages);

            if (guildQueueCount == 0) {
                queueContent += $"Query Page 0 / 0 (0 songs [index @ {guild.TrackCount}])\n\n";
                queueContent += "  --- List is empty, enlist some songs or force a draft!";
                await message.ModifyAsync($"```{queueContent}```");
                return;
            }

            queueContent += $"Query Page {targetPage} / {pages} ({guildQueueCount} songs [index @ {guild.TrackCount}])\n\n";

            List<GuildQueueItem> pageContents = guildQueueQuery
                .Include(x => x.Playlist) // Ensure Playlist is included.
                .Page(targetPage, ITEMS_PER_PAGE)
                .ToList();
            ulong? currentPlaylist = null;

            string?[] dateFormatArr = new string?[] { null, null }; // "dd/MM/yy";
            if (showDate) dateFormatArr[0] = "dd/MM/yy";
            if (showTime) dateFormatArr[1] = "HH:mm:ss";

            string? dateFormat = string.Join(" ", dateFormatArr.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(dateFormat)) dateFormat = null;

            for (int x = 0; x < pageContents.Count(); x++) {
                var dbTrack = pageContents[x];

                GuildQueueItem? nextTrack = pageContents.ElementAtOrDefault(x + 1);

                if (dbTrack.PlaylistId == null) {
                    queueContent += " ";
                } else {
                    bool isCurrent = dbTrack.Position == currentPosition;
                    bool isPlaylistBoundary = nextTrack != null && nextTrack.PlaylistId != dbTrack.PlaylistId;
                    bool isSingleSongPlaylist = dbTrack.Playlist?.PlaylistSongCount == 1;

                    char first = isCurrent ? '+' : ' ';
                    char second = (isPlaylistBoundary || isSingleSongPlaylist) ? '/' : '|';
                    string lineSymbol = string.Concat(first, second);

                    if (currentPlaylist == dbTrack.PlaylistId) {
                        queueContent += lineSymbol;
                    } else if (currentPlaylist != dbTrack.PlaylistId) {
                        queueContent += $" /Playlist: {dbTrack.Playlist?.Title} \n";

                        queueContent += lineSymbol;
                    } else if (currentPlaylist == null) {
                        queueContent += " ";
                    } else {
                        queueContent += " ";
                    }
                }

                currentPlaylist = dbTrack.PlaylistId;

                if (dbTrack.PlaylistId == null)
                    queueContent += ((dbTrack.Position == guild.CurrentTrack && guild.IsPlaying) ? "+" : " ");

                queueContent += $"  {dbTrack.Position,4}";
                queueContent += dateFormat == null ? "" : ", " + dbTrack.CreatedAt.ToString(dateFormat);
                queueContent += $") {dbTrack.Title} - Requested by ";
                queueContent += (dbTrack.RequestedBy == null) ? "<#DELETED>" : $"{dbTrack.RequestedBy.Username}";
                queueContent += "\n";
            }

            await message.ModifyAsync($"```diff\n{queueContent}```");
        }
    }
}
