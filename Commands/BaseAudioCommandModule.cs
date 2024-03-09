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

namespace CCTavern.Commands {
    public class BaseAudioCommandModule : BaseCommandModule {
        protected readonly IAudioService audioService;

        public BaseAudioCommandModule(IAudioService audioService) {
            this.audioService = audioService;
        }

        static ValueTask<TavernPlayer> CreatePlayerAsync(IPlayerProperties<TavernPlayer, TavernPlayerOptions> properties, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(properties);

            var player = new TavernPlayer(properties);
            return ValueTask.FromResult(player);
        }

        protected async ValueTask< PlayerResult<TavernPlayer> > GetPlayerAsync(ulong guildId, ulong? voiceChannelId = null, bool connectToVoiceChannel = true) {
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

            if (result.Player != null && connectToVoiceChannel) {
                var playerManager = audioService.Players;
                var pmType = playerManager.GetType();

                var eventInfo     = pmType.GetEvent("PlayerStateChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var eventDelegate = pmType.GetField("PlayerStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(playerManager) as MulticastDelegate;
                if (eventDelegate != null) {
                    var eventArgs = new PlayerStateChangedEventArgs(result.Player, PlayerState.NotPlaying);

                    foreach (var handler in eventDelegate.GetInvocationList()) {
                        var task = handler.Method.Invoke(handler.Target, [ playerManager, eventArgs ]) as Task;

                        if (task != null)
                            await task.ConfigureAwait(false);
                    }
                }
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

        protected async ValueTask<bool> IsPlayerConnected(ulong guildId, ulong? voiceChannelId = null) {
            var playerResult = await GetPlayerAsync(guildId, voiceChannelId, connectToVoiceChannel: false).ConfigureAwait(false);

            if (playerResult.IsSuccess == false 
                    || playerResult.Status == PlayerRetrieveStatus.BotNotConnected
                    || playerResult.Status == PlayerRetrieveStatus.PreconditionFailed
                    || playerResult.Player == null) {
                return false;
            }

            return true;
        }
    }
}
