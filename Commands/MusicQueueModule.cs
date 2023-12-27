using CCTavern.Database;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using LinqKit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class MusicQueueModule : BaseCommandModule {
        const int ITEMS_PER_PAGE = 10;

        public MusicBot Music { private get; set; }

        private ILogger _logger;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicQueueModule>();
                return _logger;
            }
        }

        [Command("shuffle"), Aliases("sh")]
        [Description("Shuffle the music in the queue")]
        [RequireGuild]
        public async Task ToggleGuildShuffle(CommandContext ctx, 
            [Description("Automatically play next song on the queue from where it stopped (triggered if = yes, 1, true)")]
            string enableShuffle_str = ""
        ) {
            var shuffleEnabled_str_lwr = enableShuffle_str.Trim().ToLower();
            bool shuffleEnabled = string.IsNullOrWhiteSpace(shuffleEnabled_str_lwr) ? false : 
                ( shuffleEnabled_str_lwr[0] == 'y' || shuffleEnabled_str_lwr[0] == '1' 
                    || shuffleEnabled_str_lwr[0] == 't' );

           

            // Get the guild
            var db    = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            if (string.IsNullOrWhiteSpace(shuffleEnabled_str_lwr) || shuffleEnabled_str_lwr[0] == '?') {
                if (MusicBot.GuildStates.ContainsKey(guild.Id) == false) {
                    await ctx.RespondAsync("Shuffle disabled.");
                    return;
                }

                await ctx.RespondAsync(MusicBot.GuildStates[guild.Id].ShuffleEnabled ? "Shuffle enabled." : "Shuffle disabled.");
                return;
            }

            var guildQueueQuery = db.GuildQueueItems.Where(x => x.GuildId == guild.Id && x.IsDeleted == false);
            var queueCount = await guildQueueQuery.CountAsync();

            if (queueCount < 10) {
                await ctx.RespondAsync("Shuffle cannot be enabled without 10 tracks in the queue.");
                return;
            }

            if (MusicBot.GuildStates.ContainsKey(guild.Id) == false) 
                MusicBot.GuildStates[guild.Id] = new GuildState(guild.Id);

            MusicBot.GuildStates[guild.Id].ShuffleEnabled = shuffleEnabled;
            
            await ctx.RespondAsync(shuffleEnabled ? "Shuffle enabled." : "Shuffle disabled.");
        }

        [Command("queue"), Aliases("q")]
        [Description("Lists all songs in the music queue")]
        [RequireGuild]
        public async Task GetQueue(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown. (Supports relative +1, -12) ")]
            string TargetPageString = null!,
            [Description("Show date")]
            bool showDate = false,
            [Description("Show time")]
            bool showTime = false
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Check if the current guild has temporary music
            if (Music.TemporaryTracks.ContainsKey(ctx.Guild.Id))
                queueContent += "*** Temporary queue exists, pleasue use \"queuetemp\" to view temporary queue.";

            // Get the guild
            var db    = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);
            var currentPosition = await db.GuildQueueItems.Where(qi => qi.Position <= guild.CurrentTrack).CountAsync(); 

            var targetPage = (int)Math.Ceiling((decimal)currentPosition / ITEMS_PER_PAGE);

            // Parse user's target page
            if (TargetPageString != null) {
                TargetPageString = TargetPageString.Trim();
                int output;

                try {
                    if (TargetPageString.StartsWith("+")) {
                        string strToConvert = TargetPageString[1..];
                        if (int.TryParse(strToConvert, out output)) {
                            targetPage += output;
                        }
                    } else if (TargetPageString.StartsWith("-")) {
                        string strToConvert = TargetPageString[1..];
                        if (int.TryParse(strToConvert, out output)) {
                            targetPage -= output;
                        }
                    } else if (int.TryParse(TargetPageString, out output)) {
                        targetPage = output;
                    }
                } catch { }
            }

            if (targetPage < 1) targetPage = 1;

            var guildQueueQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id && x.IsDeleted == false);
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
            if (showTime) dateFormatArr[1] = "hh:mm:ss";

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
            await GetQueue(ctx, TargetPageString, true, false);
        }

        [Command("queuedt"), Aliases("qdt")]
        [Description("Lists all songs in the music queue with date")]
        [RequireGuild]
        public async Task GetQueueDateTime(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            string TargetPageString = null!
        ) {
            await GetQueue(ctx, TargetPageString, true, true);
        }

        [Command("queuet"), Aliases("qt")]
        [Description("Lists all songs in the music queue with date")]
        [RequireGuild]
        public async Task GetQueueTime(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            string TargetPageString = null!
        ) {
            await GetQueue(ctx, TargetPageString, false, true);
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

            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            if (songIndex > guild.TrackCount) {
                await ctx.Message.RespondAsync($"Unable to set next song to `{songIndex}`, Maximum available track number is `{guild.TrackCount}`.");
                return;
            }

            guild.NextTrack = songIndex;
            await db.SaveChangesAsync();
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
