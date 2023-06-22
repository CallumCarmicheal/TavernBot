using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using DSharpPlus.Lavalink;
using CCTavern.Logger;
using Microsoft.Extensions.Logging;
using DSharpPlus.Entities;
using DSharpPlus;

namespace CCTavern.Commands.Test
{
    public class TestMusicPlayModule : BaseCommandModule
    {
        public MusicBot Music { private get; set; }

        private ILogger _logger;
        private ILogger logger
        {
            get
            {
                if (_logger == null) _logger = Program.LoggerFactory.CreateLogger<TestMusicPlayModule>();
                return _logger;
            }
        }

        [Command("play")]
        [Description("Play music")]
        public async Task Play(CommandContext ctx, [RemainingText] string search)
        {
            logger.LogInformation(TavernLogEvents.MBPlay, "Play Music: " + search);

            // Check if we have a valid voice state
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var voiceState = ctx.Member.VoiceState;
            if (voiceState == null) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            if (voiceState.Channel.GuildId != ctx.Guild.Id) {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return;
            }

            var node = lava.ConnectedNodes.Values.First();
            var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            var loadResult = await node.Rest.GetTracksAsync(search);

            // If something went wrong on Lavalink's end                          
            if (loadResult.LoadResultType == LavalinkLoadResultType.LoadFailed
                //or it just couldn't find anything.
                || loadResult.LoadResultType == LavalinkLoadResultType.NoMatches)
            {
                await ctx.RespondAsync($"Track search failed for {search}.");
                return;
            }

            var track = loadResult.Tracks.First();

            await conn.PlayAsync(track);
            await ctx.RespondAsync($"Enqueued `{track.Title}` in position `<TODO QUEUE SYSTEM>`.");
        }



    }
}
