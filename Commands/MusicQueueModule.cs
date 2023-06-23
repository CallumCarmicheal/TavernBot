using CCTavern.Database;

using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
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
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicCommandModule>();
                return _logger;
            }
        }

        [Command("queue")]
        public async Task GetQueue(CommandContext ctx, int Page = -1) {
            var message = await ctx.RespondAsync("Loading queue...");
            string queueContent = "";

            // Get the guild
            var db    = new TavernContext();
            var guild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            var currentPosition = guild.CurrentTrack;

            var targetPage = (int)Math.Ceiling((currentPosition - 1) / (double)ITEMS_PER_PAGE);
            if (targetPage < 1) targetPage = 1;
            if (Page != -1)     targetPage = Page;

            var guildQueueQuery = db.GuildQueueItems.Include(p => p.RequestedBy).Where(x => x.GuildId == guild.Id);
            var guildQueueCount = await guildQueueQuery.CountAsync();
            var pages           = (int)Math.Ceiling(guildQueueCount / (double)ITEMS_PER_PAGE);
            targetPage          = Math.Clamp(targetPage, 0, pages);

            queueContent += $"Queue Page {targetPage} / {pages} ({guildQueueCount} songs [index @ {guild.TrackCount}])\n\n";

            var pageContents = guildQueueQuery.Page(targetPage, ITEMS_PER_PAGE);

            foreach (var song in pageContents) {
                queueContent += " " + ((song.Position == guild.CurrentTrack) ? "*" : " "); 
                queueContent += $"{song.Position,4}) ";
                queueContent += $"{song.Title} - Requested by ";
                queueContent += (song.RequestedBy == null) ? "<#DELETED>" : $"{song.RequestedBy.DisplayName}\n";
            }

            await message.ModifyAsync($"```{queueContent}```");
        }
    }
}
