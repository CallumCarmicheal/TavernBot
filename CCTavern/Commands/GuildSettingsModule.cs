using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;

using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

using Lavalink4NET;

using Microsoft.Extensions.Logging;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    internal class GuildSettingsModule : BaseAudioCommandModule {
        private readonly ILogger<MusicCommandModule> logger;

        public GuildSettingsModule(ILogger<MusicCommandModule> logger, IAudioService audioService, MusicBotHelper mbHelper) 
                : base(audioService, mbHelper) {
            this.logger = logger;
        }

        [Command("setPrefixes"), Aliases("sspfx")]
        [Description("Set bot prefixes, split prefixes by semi-colon. Escape semi-colon with a backslash.")]
        public async Task SetServerPrefixes(CommandContext ctx,
            [Description("Prefixes (multiple supported) split by a \";\", if you want to use a semi-colon escape it with a backslash like \"\\;\".")]
            string prefixes
        ) {
            logger.LogInformation(TLE.MBPlay, "Setting server prefixes to " + prefixes);

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

        [Command("setMusicChannel"), Aliases("ssmc")]
        [Description("Set music channel for outputting messages")]
        public async Task SetMusicChannel(CommandContext ctx,
            [Description("Discord channel to ouput default messages to.")]
            DiscordChannel channel
        ) {
            logger.LogInformation(TLE.MBPlay, "Setting default music channel for {0} to {1}", channel.Guild.Name, channel.Name);

            var db = new TavernContext();

            Guild dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            dbGuild.MusicChannelId = channel.Id;
            dbGuild.MusicChannelName = channel.Name;
            await db.SaveChangesAsync();

            await ctx.RespondAsync($"Updated music output channel to <#{channel.Id}>.");
        }

        [Command("setLeaveAfterPlaylist"), Aliases("sslap")]
        [Description("If true the bot will leave once playlist is finished.")]
        public async Task SetLeaveAfterPlaylist(CommandContext ctx,
            [Description("If the bot will leave after finished playing music / current playlist.")]
            bool leaveAfterPlaylist
        ) {
            logger.LogInformation(TLE.MBPlay, "Setting LeaveAfterPlaylist for {0} to {1}", ctx.Guild.Name, leaveAfterPlaylist);

            var db = new TavernContext();

            Guild dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);

            dbGuild.LeaveAfterQueue = leaveAfterPlaylist;
            await db.SaveChangesAsync();

            await ctx.RespondAsync($"Updated leave after playlist to " + (leaveAfterPlaylist
                ? "disconnect after finished playing music."
                : "disconnect after some time of inactivity."));
        }
    }
}
