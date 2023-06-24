using CCTavern.Database;
using CCTavern.Logger;

using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using Microsoft.Extensions.Logging;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class GuildSettingsModule : BaseCommandModule {
        public MusicBot Music { private get; set; }

        private ILogger _logger;
        private ILogger logger {
            get {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<MusicCommandModule>();
                return _logger;
            }
        }

        [Command("setPrefixes"), Aliases("spfx")]
        [Description("Set bot prefixes, split prefixes by semi-colon. Escape semi-colon with a backslash.")]
        public async Task SetServerPrefixes(CommandContext ctx,
            [Description("Prefixes (multiple supported) split by a \";\", if you want to use a semi-colon escape it with a backslash like \"\\;\".")]
            string prefixes
        ) {
            logger.LogInformation(TavernLogEvents.MBPlay, "Setting server prefixes to " + prefixes);

            // Split prefixes
            List<string> prefixesList = prefixes.SplitWithTrim(';', '\\', true).ToList();
            string joinedPrefixes = string.Join(Constants.PREFIX_SEPERATOR.ToString()
                , prefixesList.Select(x => x.Replace(Constants.PREFIX_SEPERATOR.ToString(), "\\" + Constants.PREFIX_SEPERATOR)));

            var db = new TavernContext();
            Guild dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);
            dbGuild.Prefixes = joinedPrefixes;
            await db.SaveChangesAsync();

            var prefixListEnumerable = prefixesList.AsEnumerable();

            if (Program.ServerPrefixes.ContainsKey(ctx.Guild.Id)) {
                Program.ServerPrefixes[ctx.Guild.Id] = prefixListEnumerable;
            } else {
                Program.ServerPrefixes.Add(ctx.Guild.Id, prefixListEnumerable);
            }

            await ctx.RespondAsync($"Updated prefixes to {joinedPrefixes}.");
        }

        [Command("setMusicChannel"), Aliases("smc")]
        [Description("Set music channel for outputting messages")]
        public async Task SetMusicChannel(CommandContext ctx,
            [Description("Discord channel to ouput default messages to.")]
            DiscordChannel channel
        ) {
            logger.LogInformation(TavernLogEvents.MBPlay, "Setting default music channel for {0} to {1}", channel.Guild.Name, channel.Name);

            var db = new TavernContext();

            Guild dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            dbGuild.MusicChannelId = channel.Id;
            dbGuild.MusicChannelName = channel.Name;
            await db.SaveChangesAsync();

            await ctx.RespondAsync($"Updated music output channel to <#{channel.Id}>.");
        }

    }
}
