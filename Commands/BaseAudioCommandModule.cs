using DSharpPlus.CommandsNext;

using Lavalink4NET.Players.Queued;
using Lavalink4NET.Players;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lavalink4NET;
using System.Threading;
using CCTavern.Player;
using DSharpPlus.Entities;
using Microsoft.Extensions.Options;
using MySqlX.XDevAPI.Common;
using ZstdSharp.Unsafe;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Integrations.ExtraFilters;
using System.Numerics;
using Lavalink4NET.Player;
using Lavalink4NET.Filters;

namespace CCTavern.Commands {
    public class BaseAudioCommandModule : BaseCommandModule {
        protected readonly IAudioService audioService;
        protected readonly MusicBotHelper mbHelper;

        public BaseAudioCommandModule(IAudioService audioService, MusicBotHelper mbHelper) {
            this.audioService = audioService;
            this.mbHelper = mbHelper;
        }

        protected async ValueTask< (PlayerResult<TavernPlayer>, bool isPlayerConnected) > GetPlayerAsync(ulong guildId, ulong? voiceChannelId = null, bool connectToVoiceChannel = true) {
            return await mbHelper.GetPlayerAsync(guildId, voiceChannelId, connectToVoiceChannel);
        }

        protected string GetPlayerErrorMessage(PlayerRetrieveStatus status) => mbHelper.GetPlayerErrorMessage(status);
    }
}
