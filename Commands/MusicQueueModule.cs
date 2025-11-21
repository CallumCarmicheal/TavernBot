using CCTavern.Database;
using CCTavern.Player;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using EmbedIO.Utilities;

using Lavalink4NET;

using LinqKit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Org.BouncyCastle.Asn1.Cms;

using Swan.Logging;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

using static CCTavern.Player.YoutubeChaptersParser;

namespace CCTavern.Commands {
    internal class MusicQueueModule : BaseAudioCommandModule {
        private readonly ILogger<MusicQueueModule> logger;

        const int ITEMS_PER_PAGE = 10;

        public MusicQueueModule(MusicBotHelper mbHelper, IAudioService audioService, ILogger<MusicQueueModule> logger)
                : base(audioService, mbHelper) {
            this.logger = logger;
        }

        [Command("shuffle"), Aliases("sh")]
        [Description("Shuffle the music in the queue")]
        [RequireGuild]
        public async Task ToggleGuildShuffle(CommandContext ctx,
            [Description("True values [yes, 1, true, on]")]
            string flag_str = ""
        ) {
            var flag_str_lwr = flag_str.Trim().ToLower();
            bool flag = string.IsNullOrWhiteSpace(flag_str_lwr) ? false :
                (flag_str_lwr[0] == 'y' || flag_str_lwr[0] == '1' || flag_str_lwr[0] == 't'
                || (flag_str_lwr[0] == 'o' && flag_str_lwr[1] == 'n'));

            // Get the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            if (string.IsNullOrWhiteSpace(flag_str_lwr) || flag_str_lwr[0] == '?') {
                if (mbHelper.GuildStates.ContainsKey(guild.Id) == false) {
                    await ctx.RespondAsync("Shuffle disabled.");
                    return;
                }

                await ctx.RespondAsync(mbHelper.GuildStates[guild.Id].ShuffleEnabled ? "Shuffle enabled." : "Shuffle disabled.");
                return;
            }

            var guildQueueQuery = db.GuildQueueItems.Where(x => x.GuildId == guild.Id && x.IsDeleted == false);
            var queueCount = await guildQueueQuery.CountAsync();

            if (queueCount < 10) {
                await ctx.RespondAsync("Shuffle cannot be enabled without 10 tracks in the queue.");
                return;
            }

            if (mbHelper.GuildStates.ContainsKey(guild.Id) == false)
                mbHelper.GuildStates[guild.Id] = new GuildState(guild.Id);

            // Check if the flag is empty then we are to toggle.
            if (string.IsNullOrWhiteSpace(flag_str)) {
                mbHelper.GuildStates[guild.Id].ShuffleEnabled = !mbHelper.GuildStates[guild.Id].ShuffleEnabled;
            } else {
                mbHelper.GuildStates[guild.Id].ShuffleEnabled = flag;
            }

            await ctx.RespondAsync(mbHelper.GuildStates[guild.Id].ShuffleEnabled ? "Shuffle enabled." : "Shuffle disabled.");
        }

        [Command("queue"), Aliases("q")]
        [Description("Lists all songs in the music queue")]
        [RequireGuild]
        public async Task GetQueue(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown. (Supports relative +1, -12) ")]
            string targetPageString = null!,
            [Description("Show date")]
            bool showDate = false,
            [Description("Show time")]
            bool showTime = false
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Check if the current guild has temporary music
            if (mbHelper.TemporaryTracks.ContainsKey(ctx.Guild.Id))
                queueContent += "*** Temporary queue exists, pleasue use \"queuetemp\" to view temporary queue.";

            // Get the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);
            var guildQueueQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(qi => qi.GuildId == guild.Id && qi.IsDeleted == false);
            var currentPosition = await guildQueueQuery.Where(qi => qi.Position <= guild.CurrentTrack).CountAsync();

            var targetPage = (int)Math.Ceiling((decimal)currentPosition / ITEMS_PER_PAGE);

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

            if (targetPage < 1) targetPage = 1;

            var guildQueueCount = await guildQueueQuery.CountAsync();
            var pages = (int)Math.Ceiling(guildQueueCount / (double)ITEMS_PER_PAGE);
            targetPage = pages == 0 ? 0 : Math.Clamp(targetPage, 1, pages);

            if (guildQueueCount == 0) {
                queueContent += $"Queue Page 0 / 0 (0 songs [index @ {guild.TrackCount}])\n\n";
                queueContent += "  --- Queue is empty, enlist some songs or force a draft!";
                await message.ModifyAsync($"```{queueContent}```");
                return;
            }

            queueContent += $"Queue Page {targetPage} / {pages} ({guildQueueCount} songs [index @ {guild.TrackCount}])\n\n";

            List<GuildQueueItem> pageContents = guildQueueQuery
                .OrderBy(x => x.Position)
                    .Include(x => x.Playlist)
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
                    var lineSymbol =
                        (nextTrack != null && nextTrack.PlaylistId != dbTrack.PlaylistId)
                        || (dbTrack.Playlist?.PlaylistSongCount == 1)
                        ? "/" : "|";

                    if (currentPlaylist == dbTrack.PlaylistId) {
                        queueContent += lineSymbol;
                    } else if (currentPlaylist != dbTrack.PlaylistId) {
                        queueContent += $"/ Playlist: {dbTrack.Playlist?.Title} \n";

                        queueContent += lineSymbol;
                    } else if (currentPlaylist == null) {
                        queueContent += " ";
                    } else {
                        queueContent += " ";
                    }
                }

                currentPlaylist = dbTrack.PlaylistId;

                queueContent += " " + ((dbTrack.Position == guild.CurrentTrack && guild.IsPlaying) ? "*" : " ");
                queueContent += $" {dbTrack.Position,4}";
                queueContent += dateFormat == null ? "" : ", " + dbTrack.CreatedAt.ToString(dateFormat);
                queueContent += $") {dbTrack.Title} - Requested by ";
                queueContent += (dbTrack.RequestedBy == null) ? "<#DELETED>" : $"{dbTrack.RequestedBy.Username}";
                queueContent += "\n";
            }

            await message.ModifyAsync($"```{queueContent}```");
        }

        [Command("queued"), Aliases("qd")]
        [Description("Lists all songs in the music queue with date")]
        [RequireGuild]
        public async Task GetQueueDate(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            string TargetPageString = null!
        ) {
            await GetQueue(ctx, TargetPageString, showDate: true, showTime: false);
        }

        [Command("queuedt"), Aliases("qdt")]
        [Description("Lists all songs in the music queue with date")]
        [RequireGuild]
        public async Task GetQueueDateTime(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            string TargetPageString = null!
        ) {
            await GetQueue(ctx, TargetPageString, showDate: true, showTime: true);
        }

        [Command("queuet"), Aliases("qt")]
        [Description("Lists all songs in the music queue with date")]
        [RequireGuild]
        public async Task GetQueueTime(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            string TargetPageString = null!
        ) {
            await GetQueue(ctx, TargetPageString, showDate: false, showTime: true);
        }

        [Command("remove"), Aliases("delete")]
        [Description("Delete a song from the queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task DeleteSongFromQueue(CommandContext ctx,
            [Description("Position of the song to be removed from the queue")]
            ulong songIndex
        ) {
            // Get the guild
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            CachedUser? dbUser = null;
            if (ctx.Member != null)
                dbUser = await db.GetOrCreateCachedUser(dbGuild, ctx.Member);

            // Get the song
            var guildQueueQuery = db.GuildQueueItems
                .Include(p => p.RequestedBy)
                .Where(x => x.GuildId == dbGuild.Id && x.IsDeleted == false && x.Position == songIndex);

            // Check if the song doesn't exist
            if (await guildQueueQuery.AnyAsync() == false) {
                var emoji = DiscordEmoji.FromName(ctx.Client, ":question:");
                await ctx.Message.CreateReactionAsync(emoji);
                return;
            }

            // Make sure we only have one in our count
            int count = await guildQueueQuery.CountAsync();
            if (count > 1) {
                await ctx.RespondAsync($"Alright so heres the funny thing, this delete function should only find 1 song... I uhh found {count}.");
                return;
            }

            // Get the song
            var dbTrack = await guildQueueQuery.FirstAsync();
            await ctx.RespondAsync($"Successfully removed {await dbTrack.GetTagline(db, true)}.");

            dbTrack.IsDeleted = true;
            dbTrack.DeletedById = dbUser?.Id;
            await db.SaveChangesAsync();
        }

        [Command("setnext"), Aliases("sn", "jumpnext", "jn")]
        [Description("Set the next track index to play after the current song funishes")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task SetNextPlaylistTrackIndex(CommandContext ctx,
            [Description("Next song index to play")]
            ulong songIndex
        ) {
            // Clamp to 1
            if (songIndex <= 0) songIndex = 1;

            // Check if the user is in the voice channel
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            (var playerQuery, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, connectToVoiceChannel: false).ConfigureAwait(false);
            if (playerIsConnected == false || playerQuery.Player == null) {
                await ctx.RespondAsync("Music bot is not connected.");
                return;
            }

            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            if (songIndex > dbGuild.TrackCount) {
                await ctx.Message.RespondAsync($"Unable to set next song to `{songIndex}`, Maximum available track number is `{dbGuild.TrackCount}`.");
                return;
            }

            GuildState guildState;

            if (mbHelper.GuildStates.ContainsKey(dbGuild.Id) == false)
                mbHelper.GuildStates.Add(dbGuild.Id, guildState = new GuildState(dbGuild.Id));
            else guildState = mbHelper.GuildStates[dbGuild.Id];

            guildState.SetNextFlag = true;
            dbGuild.NextTrack = songIndex;
            await db.SaveChangesAsync();
        }


        [Command("repeat")]
        [Description("Repeat current playing song")]
        [RequireGuild]
        public async Task ToggleGuildRepeat(CommandContext ctx,
            [Description("True values [yes, 1, true, on]")]
            string flag_str = ""
        ) {
            var flag_str_lwr = flag_str.Trim().ToLower();
            bool flag = string.IsNullOrWhiteSpace(flag_str_lwr) ? false :
                (flag_str_lwr[0] == 'y' || flag_str_lwr[0] == '1' || flag_str_lwr[0] == 't'
                || (flag_str_lwr[0] == 'o' && flag_str_lwr[1] == 'n'));

            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var voiceState = ctx.Member.VoiceState;
            var channel = voiceState.Channel;
            if (voiceState.Channel.GuildId != ctx.Guild.Id) {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return;
            }

            (var playerQuery, var isPlayerConnected) = await GetPlayerAsync(ctx.Guild.Id, ctx.Member?.VoiceState.Channel.Id, connectToVoiceChannel: false);
            if (isPlayerConnected == false || playerQuery.Player == null) {
                await ctx.RespondAsync("Music bot is not connected.");
                return;
            }

            // Get the guild
            var db = new TavernContext();
            var dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);
            GuildState guildState;

            if (mbHelper.GuildStates.ContainsKey(dbGuild.Id) == false)
                mbHelper.GuildStates.Add(dbGuild.Id, guildState = new GuildState(dbGuild.Id));
            else guildState = mbHelper.GuildStates[dbGuild.Id];

            // Check if the flag is empty then we are to toggle.
            if (string.IsNullOrWhiteSpace(flag_str)) {
                guildState.RepeatEnabled = !guildState.RepeatEnabled;
            } else {
                guildState.RepeatEnabled = flag;
            }

            if (guildState.RepeatEnabled)
                await ctx.RespondAsync($"Repeat enabled.");
            else
                await ctx.RespondAsync($"Repeat disabled.");
        }

        [Command("ytsettl")]
        [Description("[YT] Set sub-track playlist")]
        [RequireGuild]
        public async Task SetCustomYTTrackList(CommandContext ctx,
            [Description("First line is video id, every line after is used to parse the positions.")]
            string ytVideoId,

            [Description("A list of all tracks with the timestamp followed by the track name / display text.")]
            [RemainingText] string trackPositions = ""
        ) {
            logger.LogDebug("ytsettl sent!");
            if (string.IsNullOrWhiteSpace(ytVideoId)) {
                await ctx.RespondAsync("No tracks or video id has been attached.");
                return;
            }

            // Check if the ytVideoId is a url
            Uri? trackUri = null;
            bool result = Uri.TryCreate(ytVideoId, UriKind.Absolute, out trackUri)
                && (trackUri.Scheme == Uri.UriSchemeHttp || trackUri.Scheme == Uri.UriSchemeHttps);

            if (result && trackUri != null
                    && (trackUri.Host == "youtube.com" || trackUri.Host == "www.youtube.com")) {
                var uriQuery = HttpUtility.ParseQueryString(trackUri.Query);
                if (uriQuery.ContainsKey("v"))
                    ytVideoId = uriQuery["v"]!;
            }

            var tracks = trackPositions.Split('\n')
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            // Check if we have any tracks
            if (tracks.Count <= 1) {
                await ctx.RespondAsync("There are not enough tracks attached");
                return;
            }

            // Try to parse the track lists
            var slTracks = new SortedDictionary<TimeSpan, string>();

            foreach (var trackString in tracks) {
                // Get Timestamps
                var matchTs = YoutubeChaptersParser.RgxTimestampMatch.Match(trackString);
                if (matchTs.Success) {
                    // Parse into timestamp
                    var ts = matchTs.Value.TryParseTimeStamp();
                    if (ts == null) continue;

                    slTracks.Add(ts.Value, trackString);
                }
            }

            // Add to the database
            if (slTracks.Count > 1) {
                // Add to the database
                var dbCtx = new TavernContext();

                foreach (var track in slTracks) {
                    var tpp = new TrackPlaylistPosition() {
                        TrackSource = "youtube",
                        TrackSourceId = ytVideoId,
                        Position = track.Key,
                        DisplayText = track.Value
                    };
                    await dbCtx.TrackPlaylistPositions.AddAsync(tpp).ConfigureAwait(false);
                }

                await dbCtx.SaveChangesAsync().ConfigureAwait(false);
                await ctx.RespondAsync($"Added {slTracks.Count} tracks to youtube {ytVideoId}.");
            } else {
                await ctx.RespondAsync("There are not enough tracks (min 2).");
                return;
            }
        }

        /*/[Command("searchQueue"), Aliases("sq")]
        [Description("Search queue for ")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task SearchQueue(CommandContext ctx,
            [Description("Fields to search")]
            [RemainingText]
            string searchFields
        ) {

            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild, false);

            var predicate = PredicateBuilder.New<GuildQueueItem>();

            var searchFieldsSplit = searchFields.Split("(?<!\\\\),");
            bool hasSearch = false;

            foreach (var field in searchFieldsSplit) {
                var searchSplit = field.Split("(?<!\\\\)=");

                for (int x = 0; x < searchSplit.Length; x++)
                    searchSplit[x] = searchSplit[x].Trim();


                // TODO: Finish user search queue functionality
                switch (field) {
                    case "user":
                        predicate.Or(p => p.RequestedBy.DisplayName == "");
                        break;
                    case "title":
                        break;
                    case "author":

                        break;
                }
            } 
        } //*/
    }
}
