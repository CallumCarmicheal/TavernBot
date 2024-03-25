using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;

using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;

using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace CCTavern.Commands {
    enum TrackRequestedPlayMode {
        Single,
        Playlist,
        Expired
    }

    internal class MusicPlayModule : BaseAudioCommandModule {
        private readonly ILogger<MusicPlayModule> logger;

        public MusicPlayModule(MusicBotHelper mbHelper, ILogger<MusicPlayModule> logger
                , IAudioService audioService) : base(audioService, mbHelper) {
            this.logger = logger;
        }

        static bool IsUrl(string input) {
            return Uri.TryCreate(input, UriKind.Absolute, out var uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        [Command("play"), Aliases("p")]
        [Description("Play music using a search")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task Play(CommandContext ctx, [RemainingText] string search) {
            logger.LogInformation(TLE.MBPlay, "Play Music: " + search);

            var voiceState = ctx.Member?.VoiceState;
            if (voiceState == null || voiceState.Channel == null || ctx.Member == null) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            if (voiceState.Channel.GuildId != ctx.Guild.Id) {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return;
            }

            var voiceChannel = voiceState.Channel;

            (var oldPlayerResult, var wasPlayerConnected) = await GetPlayerAsync(ctx.Guild.Id, voiceChannel.Id, connectToVoiceChannel: false).ConfigureAwait(false);
            (var playerResult, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, voiceChannel.Id, connectToVoiceChannel: true).ConfigureAwait(false);
            if (playerIsConnected == false || playerResult.Player == null) {
                await ctx.RespondAsync(GetPlayerErrorMessage(playerResult.Status));
                return;
            }

            // Check if we are joining the channel
            if (playerIsConnected && !wasPlayerConnected) {
                await ctx.RespondAsync($"Connected to <#{ctx.Member?.VoiceState.Channel.Id}>.");
            }

            var db = new TavernContext();
            var player = playerResult.Player;
            TrackLoadResult? trackQueryResults;

            if (IsUrl(search)) {
                trackQueryResults = await audioService.Tracks
                    .LoadTracksAsync(search, TrackSearchMode.None)
                    .ConfigureAwait(false);
            } else {
                trackQueryResults = await audioService.Tracks
                    .LoadTracksAsync(search, TrackSearchMode.YouTube)
                    .ConfigureAwait(false);
            }

            if (trackQueryResults.HasValue && trackQueryResults.Value.IsPlaylist) {
                var prompt = await _Play_PromptForPlaylist(ctx);

                switch (prompt) {
                case TrackRequestedPlayMode.Single:
                    if (trackQueryResults.Value.Playlist != null) {
                        await _Play_Single(ctx, db, player, trackQueryResults.Value.Playlist.SelectedTrack);
                    } else {
                        await _Play_Single(ctx, db, player, trackQueryResults.Value.Track);
                    }
                    
                    return;
                case TrackRequestedPlayMode.Playlist:
                    await _Play_Playlist(ctx, db, player, trackQueryResults.Value);
                    return;
                case TrackRequestedPlayMode.Expired: break;
                }
            }

            if (trackQueryResults.HasValue == false || trackQueryResults.Value.HasMatches == false) {
                await ctx.RespondAsync($"Track search failed for {search}.");
                return;
            }

            await _Play_Single(ctx, db, player, trackQueryResults.Value.Track);
        }

        private async Task<TrackRequestedPlayMode> _Play_PromptForPlaylist(CommandContext ctx) {
            // Set a static time 30 seconds from now so if the message needs to be reset
            // it still waits 30 seconds from the original message.
            var waitTimespan = TimeSpan.FromSeconds(30);
            var btnExpired = DateTime.Now.Add(waitTimespan);

        waitForButton:
            long ticks = DateTime.Now.Ticks;
            byte[] bytes = BitConverter.GetBytes(ticks);
            string interactionId = Convert.ToBase64String(bytes).Replace('+', '_').Replace('/', '-').TrimEnd('=');

            var singleButton = new DiscordButtonComponent(ButtonStyle.Success, $"single{interactionId}", "Add single song");
            var playlistButton = new DiscordButtonComponent(ButtonStyle.Danger, $"playlist{interactionId}", "Add playlist");

            var builder = new DiscordMessageBuilder()
                .WithContent("You added a playlist, do you want to add the whole thing?")
                .AddComponents(singleButton, playlistButton);

            var buttonMessage = await ctx.RespondAsync(builder);
            var interactivity = ctx.Client.GetInteractivity();
            var result        = await interactivity.WaitForButtonAsync(buttonMessage, waitTimespan);

            if (result.TimedOut) {
                await buttonMessage.DeleteAsync();
                await ctx.RespondAsync("Track was not added to queue, interactive buttons timed out. (30+ seconds with no response).");
                return TrackRequestedPlayMode.Expired;
            }

            // Dirty hack to ensure only the message sender clicks the button.
            else if (result.Result.User.Id != ctx.User.Id) {
                await buttonMessage.DeleteAsync();

                if (DateTime.Now >= btnExpired) {
                    await ctx.RespondAsync("Track was not added to queue, interactive buttons timed out. (30+ seconds with no response).");
                    return TrackRequestedPlayMode.Expired;
                }

                goto waitForButton;
            }

            // Delete the button message.
            await buttonMessage.DeleteAsync();

            if (result.Result.Id == $"single{interactionId}") {
                return TrackRequestedPlayMode.Single;
            } else if (result.Result.Id == $"playlist{interactionId}") {
                return TrackRequestedPlayMode.Playlist;
            } else {
                return TrackRequestedPlayMode.Expired;
            }
        }

        private async Task _Play_Single(CommandContext ctx, TavernContext db, TavernPlayer player, LavalinkTrack? track) {
            if (track is null) {
                await ctx.RespondAsync($"Failed to parse track.");
                return;
            }

            // Check if the member is null
            if (ctx.Member == null) {
                await ctx.RespondAsync($"CRITICAL ERROR, Unable to retrieve author Id.");
                return;
            }

            var isPlayEvent = player.State == Lavalink4NET.Players.PlayerState.NotPlaying;
            var trackPosition = await mbHelper.EnqueueTrack(track, ctx.Channel, ctx.Member, null, isPlayEvent);
            await ctx.RespondAsync($"Enqueued `{track.Title}` in position `{trackPosition}`.");

            if (isPlayEvent) {
                var dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);
                dbGuild.CurrentTrack = trackPosition;
                await db.SaveChangesAsync();

                await player.PlayAsync(track).ConfigureAwait(false);
            }
        }

        private async Task _Play_Playlist(CommandContext ctx, TavernContext db, TavernPlayer player, TrackLoadResult trackResults) {
            // Sanity checks
            if (ctx.Member == null) {
                await ctx.RespondAsync("CRITICAL ERROR: Failed to retrieve author Id.");
                return;
            }

            if (trackResults.Playlist == null) {
                await ctx.RespondAsync("Failed to parse playlist data.");
                return;
            }

            // 
            bool isPlayEvent = player.State == Lavalink4NET.Players.PlayerState.NotPlaying;
            var dbGuild      = await db.GetOrCreateDiscordGuild(ctx.Guild);
            var requestedBy  = await db.GetOrCreateCachedUser(new Guild { Id = ctx.Guild.Id }, ctx.Member);

            // Add all the tracks to the queue
            var list = trackResults.Tracks.ToList();
            var addingMessage = await ctx.RespondAsync($"Adding `0`/`{list.Count}` tracks to playlist...");

            // Create the queue 
            var playlist = new GuildQueuePlaylist();
            playlist.Title = trackResults.Playlist.Name;
            playlist.CreatedById = requestedBy.Id;
            playlist.PlaylistSongCount = list.Count();
            db.GuildQueuePlaylists.Add(playlist);
            await db.SaveChangesAsync();

            // Loop the tracks
            for (int x = 0; x < list.Count(); x++) {
                var lt = list[x];
                var trackIdx = await mbHelper.EnqueueTrack(lt, ctx.Channel, ctx.Member, playlist, (x == 0 && isPlayEvent));

                // If we are the first track and join event then start playing it.
                if (x == 0 && isPlayEvent) {
                    dbGuild ??= await db.GetOrCreateDiscordGuild(ctx.Guild);
                    dbGuild.CurrentTrack = trackIdx;
                    await db.SaveChangesAsync();

                    await player.PlayAsync(lt).ConfigureAwait(false);
                    logger.LogInformation("Loading playlist, Playing first track.");
                }

                // Every 5 tracks update the index
                if (x % 15 == 0) {
                    // TODO: Check if the bot is finished on the queue, then make it continue playing on the next track
                    await addingMessage.ModifyAsync($"Adding `{x}`/`{list.Count()}` tracks to playlist...");
                }
            }

            await addingMessage.ModifyAsync($"Successfully added `{list.Count()}` tracks to playlist...");
        }

        [Command("join")]
        [Description("Join the current voice channel and do nothing.")]
        [RequireGuild, RequireBotPermissions(Permissions.UseVoice)]
        public async Task JoinVoice(CommandContext ctx,
            [Description("True values [yes, 1, true, resume, start]")]
            string flagStr = "f"
        ) {
            var flagStrLower = flagStr.ToLowerInvariant();
            bool resumePlaylist = flagStrLower[0] switch {
                'y' or '1' or 't' or 'r' or ('o' and 'n') => true,
                's' => flagStrLower switch {
                    "start" => true,
                    "stop" => false,
                    _ => false
                },
                _ => false
            };

            logger.LogInformation(TLE.Misc, "Join voice: Continue = {continuePlaying}", resumePlaylist);

            var voiceState = ctx.Member?.VoiceState;
            if (voiceState == null || voiceState.Channel == null || ctx.Member == null) {
                await ctx.RespondAsync("Not a valid voice channel.");
                return;
            }

            if (voiceState.Channel.GuildId != ctx.Guild.Id) {
                await ctx.RespondAsync("Not in voice channel of this guild.");
                return;
            }

            var voiceChannel = voiceState.Channel;
            (var playerResult, var playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, voiceChannel.Id, connectToVoiceChannel: false).ConfigureAwait(false);

            // If the music bot is already connected
            if (playerIsConnected && playerResult.Status != Lavalink4NET.Players.PlayerRetrieveStatus.BotNotConnected) {
                await ctx.RespondAsync("Bot is already connected to a voice channel.");
                return;
            }

            // Connect the music bot 
            (playerResult, playerIsConnected) = await GetPlayerAsync(ctx.Guild.Id, voiceChannel.Id, connectToVoiceChannel: true).ConfigureAwait(false);
            if (playerIsConnected == false || playerResult.Player == null) {
                await ctx.RespondAsync(GetPlayerErrorMessage(playerResult.Status));
                return;
            }

            await ctx.RespondAsync($"Joined <#{voiceChannel.Id}>!");

            if (resumePlaylist && playerResult.Player?.State == Lavalink4NET.Players.PlayerState.NotPlaying) {
                var db = new TavernContext();
                var dbGuild = await db.GetOrCreateDiscordGuild(ctx.Guild);
                var nextTrack = await mbHelper.getNextTrackForGuild(ctx.Guild);

                if (nextTrack != null) {
                    var track = LavalinkTrack.Parse(nextTrack.TrackString, provider: null);

                    if (track != null) {
                        dbGuild.CurrentTrack = nextTrack.Position;
                        dbGuild.NextTrack    = nextTrack.Position + 1;
                        await db.SaveChangesAsync();

                        await playerResult.Player.PlayAsync(track).ConfigureAwait(false);
                        return;
                    }
                }
            }
        }
    }
}
