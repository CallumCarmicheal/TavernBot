using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    public class MusicCmdModule : BaseCommandModule {

        public MusicBot Music { private get; set; }

        [Command("join")]
        [Description("Join the current voice channel")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task JoinVoice(CommandContext ctx, DiscordChannel channel) {
            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var node = lava.ConnectedNodes.Values.First();

            if (channel.Type != ChannelType.Voice) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            await node.ConnectAsync(channel);
            await ctx.RespondAsync($"Joined {channel.Name}!");
        }

        [Command("leave")]
        [Description("Leaves the current voice channel")]
        public async Task Leave(CommandContext ctx, DiscordChannel channel) {
            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any()) {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var node = lava.ConnectedNodes.Values.First();

            if (channel.Type != ChannelType.Voice) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            var conn = node.GetGuildConnection(channel.Guild);

            if (conn == null) {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            await conn.DisconnectAsync();
            await ctx.RespondAsync($"Left {channel.Name}!");
        }

    }
}
