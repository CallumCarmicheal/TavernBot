using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class TemporaryQueueModule : BaseCommandModule {
        const int ITEMS_PER_PAGE = 10;
        private readonly IDbContextFactory<TavernContext> databaseFactory;

        private readonly MusicBotHelper mbHelper = null!;

        public TemporaryQueueModule(MusicBotHelper mbHelper, IDbContextFactory<TavernContext> databaseFactory) {
            this.databaseFactory = databaseFactory;
            this.mbHelper = mbHelper;
        }

        private ILogger _logger = null!;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicPlayModule>();
                return _logger;
            }
        }

        [Command("playonce"), Aliases("po")]
        [Description("Play a song once using a search (**Disabled during development**)")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public Task PlayOnce(CommandContext ctx, [RemainingText] string search) {
            logger.LogInformation(TLE.MBPlay, "Play Music Once: " + search);
            throw new NotImplementedException(); // TODO: Implement
            // return default;
        }


        [Command("queuetemp"), Aliases("qtmp")]
        [Description("Lists all songs in the temporary queue")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task GetQueueTemporary(CommandContext ctx,
            [Description("Page number to view, if blank then the page containing the current track is shown.")]
            int Page = -1
        ) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Check if the current guild has temporary music
            if (!mbHelper.TemporaryTracks.ContainsKey(ctx.Guild.Id)) {
                await message.ModifyAsync($"Temporary queue is empty.");
                return;
            }

            var tempQueue = mbHelper.TemporaryTracks[ctx.Guild.Id];

            // Get the guild
            await using var db = await databaseFactory.CreateDbContextAsync();
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

                queueContent += " " + ((x == 0) ? "→" : " ");
                queueContent += $"{x,3}) ";
                queueContent += $"{dbTrack.Title} - Requested by ";

                string? requestedBy = null;

                if (dbTrack.RequestedByUser != null)
                    requestedBy = dbTrack.RequestedByUser.Username;
                else if (dbTrack.RequestedByUserId.HasValue)
                    requestedBy = (await db.Users.Where(x => x.Id == dbTrack.RequestedByUserId.Value).FirstAsync().ConfigureAwait(false)).Username;

                queueContent += requestedBy ?? "<#UNKNOWN>";
                queueContent += "\n";
            }

            await message.ModifyAsync($"```{queueContent}```");
        }
    }
}