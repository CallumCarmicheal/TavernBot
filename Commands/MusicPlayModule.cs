using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using DSharpPlus.Lavalink;

namespace CCTavern.Commands {
    internal class MusicPlayModule {
        public MusicBot Music { private get; set; }


        [Command("play"), Aliases("p")]
        [Description("Play music")]
        public async Task Play(CommandContext ctx, string search) {
            // Check if we have a valid voice state
            if (ctx.Member.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var node = Music.LavalinkNode;
            var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            var loadResult = await node.Rest.GetTracksAsync(search);

            // If something went wrong on Lavalink's end                          
            if (loadResult.LoadResultType == LavalinkLoadResultType.LoadFailed

                //or it just couldn't find anything.
                || loadResult.LoadResultType == LavalinkLoadResultType.NoMatches) {
                await ctx.RespondAsync($"Track search failed for {search}.");
                return;
            }

            var track = loadResult.Tracks.First();

            await conn.PlayAsync(track);

            await ctx.RespondAsync($"Now playing {track.Title}!");
        }

        [Command]
        [Description("Pause currently playing track")]
        public async Task Pause(CommandContext ctx) {
            if (ctx.Member.VoiceState == null || ctx.Member.VoiceState.Channel == null) {
                await ctx.RespondAsync("You are not in a voice channel.");
                return;
            }

            var lava = ctx.Client.GetLavalink();
            var node = lava.ConnectedNodes.Values.First();
            var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            if (conn.CurrentState.CurrentTrack == null) {
                await ctx.RespondAsync("There are no tracks loaded.");
                return;
            }

            await conn.PauseAsync();
        }
    }
}
