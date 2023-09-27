using CCTavern.Logger;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Commands.Test
{
    public class TestMusicCmdModule : BaseCommandModule
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

        [Command("join")]
        [Description("Join the current voice channel")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task JoinVoice(CommandContext ctx)
        {
            logger.LogInformation(TLE.Misc, "Join voice");

            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any())
            {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var voiceState = ctx.Member?.VoiceState;
            if (voiceState == null)
            {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            if (voiceState.Channel.GuildId != ctx.Guild.Id)
            {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return;
            }

            var channel = voiceState.Channel;
            var node = lava.ConnectedNodes.Values.First();

            if (channel.Type != ChannelType.Voice)
            {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            await node.ConnectAsync(channel);
            MusicBot.AnnounceJoin(channel);

            await ctx.RespondAsync($"Joined {channel.Name}!");
        }

        [Command("leave")]
        [Description("Leaves the current voice channel")]
        public async Task Leave(CommandContext ctx, DiscordChannel channel)
        {
            var lava = ctx.Client.GetLavalink();
            if (!lava.ConnectedNodes.Any())
            {
                await ctx.RespondAsync("The Lavalink connection is not established");
                return;
            }

            var node = lava.ConnectedNodes.Values.First();

            if (channel.Type != ChannelType.Voice)
            {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            var conn = node.GetGuildConnection(channel.Guild);

            if (conn == null)
            {
                await ctx.RespondAsync("Lavalink is not connected.");
                return;
            }

            await conn.DisconnectAsync();
            MusicBot.AnnounceLeave(channel);

            await ctx.RespondAsync($"Left {channel.Name}!");
        }


        [Command("pause")]
        [Description("Pause currently playing track")]
        public async Task Pause(CommandContext ctx)
        {
            if (ctx.Member?.VoiceState == null || ctx.Member.VoiceState.Channel == null)
            {
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
                await ctx.RespondAsync("There are no tracks playing.");
                return;
            }

            await conn.PauseAsync();
        }
    }
}
