﻿using CCTavern.Database;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

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

        [Command("queue"), Aliases("q")]
        [Description("Lists all songs in the music queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task GetQueue(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            int Page = -1
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Check if the current guild has temporary music
            if (Music.TemporaryTracks.ContainsKey(ctx.Guild.Id))
                queueContent += "*** Temporary queue exists, pleasue use \"queuetemp\" to view temporary queue.";

            // Get the guild
            var db    = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);
            var currentPosition = guild.CurrentTrack;

            var targetPage = (int)Math.Ceiling((currentPosition - 1) / (double)ITEMS_PER_PAGE);
            if (targetPage < 1) targetPage = 1;
            if (Page != -1)     targetPage = Page;

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

            var pageContents = guildQueueQuery
                .OrderBy(x => x.Position)
                .Include(x => x.Playlist)
                .Page(targetPage, ITEMS_PER_PAGE)
                .ToList();
            ulong? currentPlaylist = null;

            for (int x = 0; x < pageContents.Count(); x++) {
                var dbTrack = pageContents[x];

                GuildQueueItem? nextTrack = pageContents.ElementAtOrDefault(x + 1);

                if (dbTrack.PlaylistId == null) {
                    queueContent += " ";
                } else {
                    var lineSymbol = 
                        (nextTrack != null && nextTrack.PlaylistId != dbTrack.PlaylistId)
                        || (dbTrack.Playlist.PlaylistSongCount == 1)
                        ? "/" : "|";

                    if (currentPlaylist == dbTrack.PlaylistId) {
                        queueContent += lineSymbol;
                    } else if (currentPlaylist != dbTrack.PlaylistId) {
                        queueContent += $"/ Playlist: {dbTrack.Playlist.Title} \n";

                        queueContent += lineSymbol;
                    } else if (currentPlaylist == null) {
                        queueContent += " ";
                    } else {
                        queueContent += " ";
                    }
                }

                currentPlaylist = dbTrack.PlaylistId;

                queueContent += " " + ((dbTrack.Position == guild.CurrentTrack && guild.IsPlaying) ? "*" : " ");
                queueContent += $"{dbTrack.Position,3}) ";
                queueContent += $"{dbTrack.Title} - Requested by ";
                queueContent += (dbTrack.RequestedBy == null) ? "<#DELETED>" : $"{dbTrack.RequestedBy.Username}\n";
            }

            await message.ModifyAsync($"```{queueContent}```");
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
            var guildQueueQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == dbGuild.Id && x.IsDeleted == false && x.Position == songIndex);
            
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
            await ctx.RespondAsync($"Successfully removed `{dbTrack.Title}` from the queue at position `{dbTrack.Position}`.");

            dbTrack.IsDeleted = true;
            dbTrack.DeletedById = dbUser?.Id;
            await db.SaveChangesAsync();
        }


        [Command("queuetemp"), Aliases("qt")]
        [Description("Lists all songs in the temporary queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task GetQueueTemporary(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            int Page = -1
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Check if the current guild has temporary music
            if (!Music.TemporaryTracks.ContainsKey(ctx.Guild.Id)) {
                await message.ModifyAsync($"Temporary queue is empty.");
                return;
            }

            var tempQueue = Music.TemporaryTracks[ctx.Guild.Id];

            // Get the guild
            var db = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            var targetPage = 1;
            if (targetPage < 1) targetPage = 1;
            if (Page != -1) targetPage = Page;

            var guildQueueCount = tempQueue.SongCount;
            var pages = (int)Math.Ceiling(guildQueueCount / (double)ITEMS_PER_PAGE);
            targetPage = pages == 0 ? 0 : Math.Clamp(targetPage, 1, pages);

            if (guildQueueCount == 0) {
                queueContent += $"Temporary Queue Page 0 / 0 (0 songs)\n\n";
                queueContent += "  --- Queue is empty, enlist some songs or force a draft!";
                await message.ModifyAsync($"```{queueContent}```");
                return;
            }

            queueContent += $"Temporary Queue Page {targetPage} / {pages} ({guildQueueCount} songs)\n\n";

            List<GuildQueueItem> pageContents = new List<GuildQueueItem>();

            var songItemsTempList = tempQueue.SongItems.Take(ITEMS_PER_PAGE);
            foreach (var item in songItemsTempList) {
                if (item is TemporaryQueue.TemporaryPlaylist tp)
                    pageContents.AddRange(tp.Songs);

                else if (item is TemporaryQueue.TemporarySong ts)
                    pageContents.Add(ts.QueueItem);

                // Break if we reached our amount
                if (pageContents.Count >= ITEMS_PER_PAGE)
                    break;
            }

            pageContents = pageContents.Take(ITEMS_PER_PAGE).ToList();

            ulong? currentPlaylist = null;

            for (int x = 0; x < pageContents.Count(); x++) {
                var dbTrack = pageContents[x];

                GuildQueueItem? nextTrack = pageContents.ElementAtOrDefault(x + 1);

                if (dbTrack.PlaylistId == null) {
                    queueContent += " ";
                } else {
                    var lineSymbol =
                        (nextTrack != null && nextTrack.PlaylistId != dbTrack.PlaylistId)
                        || (dbTrack.Playlist.PlaylistSongCount == 1)
                        ? "/" : "|";

                    if (currentPlaylist == dbTrack.PlaylistId) {
                        queueContent += lineSymbol;
                    } else if (currentPlaylist != dbTrack.PlaylistId) {
                        queueContent += $"/ Playlist: {dbTrack.Playlist.Title} \n";

                        queueContent += lineSymbol;
                    } else if (currentPlaylist == null) {
                        queueContent += " ";
                    } else {
                        queueContent += " ";
                    }
                }

                currentPlaylist = dbTrack.PlaylistId;

                queueContent += " " + ((x == 0) ? "→" : " ");
                queueContent += $"{x,3}) ";
                queueContent += $"{dbTrack.Title} - Requested by ";

                var query = db.CachedUsers.Where(x => x.UserId == dbTrack.RequestedById && x.GuildId == guild.Id);
                CachedUser? requestedBy = null;

                if (await query.AnyAsync())
                    requestedBy = await query.FirstAsync();

                queueContent += (dbTrack.RequestedBy == null) ? "<#DELETED>" : $"{dbTrack.RequestedBy.Username}\n";
            }

            await message.ModifyAsync($"```{queueContent}```");
        }
    }
}
