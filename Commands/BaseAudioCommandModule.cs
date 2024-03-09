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

namespace CCTavern.Commands {
    public class BaseAudioCommandModule : BaseCommandModule {
        protected readonly IAudioService audioService;

        public BaseAudioCommandModule(IAudioService audioService) {
            this.audioService = audioService;
        }

        static ValueTask<TavernPlayer> CreatePlayerAsync(IPlayerProperties<TavernPlayer, TavernPlayerOptions> properties, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(properties);

            return ValueTask.FromResult(new TavernPlayer(properties));
        }

        protected async ValueTask<PlayerResult<TavernPlayer>> GetPlayerAsync(ulong guildId, ulong? voiceChannelId = null, bool connectToVoiceChannel = true) {
            var channelBehavior = connectToVoiceChannel
                ? PlayerChannelBehavior.Join
                : PlayerChannelBehavior.None;

            var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: channelBehavior);

            var result = await audioService.Players
                .RetrieveAsync< TavernPlayer, TavernPlayerOptions >(
                    guildId: guildId,
                    memberVoiceChannel: voiceChannelId,
                    playerFactory: CreatePlayerAsync,
                    options: Options.Create(new TavernPlayerOptions() {
                        VoiceChannelId = voiceChannelId
                    }),
                    retrieveOptions: retrieveOptions
                )
                .ConfigureAwait(false);

            if (!result.IsSuccess) {
                var errorMessage = result.Status switch {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                    _ => "Unknown error.",
                };

                return result;
            }

            return result;
        }

        protected string GetPlayerErrorMessage(PlayerRetrieveStatus status) {
            var errorMessage = status switch {
                PlayerRetrieveStatus.Success => "Success",
                PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                PlayerRetrieveStatus.VoiceChannelMismatch => "You are not in the same channel as the Music Bot!",
                PlayerRetrieveStatus.UserInSameVoiceChannel => "Same voice channel?",
                PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                PlayerRetrieveStatus.PreconditionFailed => "A unknown error happened: Precondition Failed."
            };

            return errorMessage;
        }
    }
}
