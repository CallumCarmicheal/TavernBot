﻿using CCTavern.Commands;
using CCTavern.Logger;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;

using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace CCTavern {
    sealed class ApplicationHost : BackgroundService {
        private readonly DiscordClient discordClient;
        private readonly IAudioService audioService;
        private readonly ITavernSettings settings;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger<ApplicationHost> logger;


        public ApplicationHost(IServiceProvider serviceProvider
                , DiscordClient discordClient
                , IAudioService audioService
                , ITavernSettings settings
                , ILogger<ApplicationHost> logger) {
            ArgumentNullException.ThrowIfNull(discordClient);
            ArgumentNullException.ThrowIfNull(audioService);

            this.discordClient = discordClient;
            this.audioService = audioService;
            this.settings = settings;
            this.serviceProvider = serviceProvider;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            discordClient.UseInteractivity(new InteractivityConfiguration() {
                PollBehaviour = PollBehaviour.KeepEmojis,
                Timeout = TimeSpan.FromSeconds(30)
            });

            // We can specify a status for our bot. Let's set it to "online" and set the activity to "with fire".
            var status = new DiscordActivity("Loading :)", ActivityType.Playing);
            await discordClient.ConnectAsync(status, UserStatus.DoNotDisturb)
                .ConfigureAwait(false);

            var commands = discordClient.UseCommandsNext(new CommandsNextConfiguration() {
                CaseSensitive = settings.PrefixesCaseSensitive,
                PrefixResolver = DiscordPrefixResolver,
                EnableMentionPrefix = true,
                DmHelp = false,
                Services = serviceProvider
            });

#if (ARCHIVAL_MODE)
            // Archival import mode
            commands.RegisterCommands<ArchiveImportModule>();
            commands.RegisterCommands<BotCommandsModule>();
            commands.RegisterCommands<MusicQueueModule>();
#else
            commands.RegisterCommands<MusicCommandModule>();
            commands.RegisterCommands<MusicPlayModule>();
            commands.RegisterCommands<GuildSettingsModule>();
            commands.RegisterCommands<BotCommandsModule>();
            commands.RegisterCommands<MusicQueueModule>();
            commands.RegisterCommands<StatusCommandModule>();
#endif

            try {
                // Initialize node connection
                await audioService
                    .WaitForReadyAsync(stoppingToken)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                status = new($"Error LLC Failure ({DateTime.Now:dd/MM/yyyy HH:mm:ss})", ActivityType.Competing);
                await discordClient.UpdateStatusAsync(status, UserStatus.Idle);

                logger.LogError(TLE.Startup, ex, $"Failed to connect to Lavalink audio service backend: {ex.Message}");
                throw;
            }

            status = new($"Ready ({DateTime.Now:dd/MM/yyyy HH:mm:ss})", ActivityType.Playing);
            await discordClient.UpdateStatusAsync(status, UserStatus.Online);

            discordClient.Zombied += DiscordClient_Zombied;
            discordClient.SocketClosed += DiscordClient_SocketClosed;

            logger.LogInformation(TLE.Startup, "Ready :)");
        }

        public override async Task StopAsync(CancellationToken cancellationToken) {
            logger.LogCritical(TLE.Disconnected, "Disconnecting ApplicationHost, StopAsync.");

            try { await discordClient.DisconnectAsync(); } catch { }
            try { await audioService.StopAsync(); } catch { }

            await base.StopAsync(cancellationToken);
        }

        private async Task DiscordClient_SocketClosed(DiscordClient sender, DSharpPlus.EventArgs.SocketCloseEventArgs e) {
            logger.LogCritical(TLE.Disconnected, "Discord API disconnected (SocketClosed), Close code: {code}, message: {message}.", e.CloseCode, e.CloseMessage);

            if (e.CloseCode == 4002) {
                logger.LogCritical(TLE.Disconnected, "Restarting the bot in 20 seconds.");
                try { await sender.DisconnectAsync().ConfigureAwait(false); } catch { }

                await Task.Delay(20000); // Wait 20 seconds.
                logger.LogCritical(TLE.Disconnected, "Shutting down...");

                Program.Shutdown();
            }

            //else if (e.CloseCode <= 4003 || (e.CloseCode >= 4005 && e.CloseCode <= 4009) || e.CloseCode >= 5000) {
            //    logger.LogCritical(TLE.Disconnected, "Attempting to reconnect the bot in 10 seconds.");
            //    await Task.Delay(20000); // Wait 20 seconds.
            //
            //    logger.LogCritical(TLE.Disconnected, "Reconnecting the bot.");
            //    await sender.ReconnectAsync().ConfigureAwait(false);
            //}
        }

        private Task DiscordClient_Zombied(DiscordClient sender, DSharpPlus.EventArgs.ZombiedEventArgs args) {
            logger.LogCritical(TLE.Disconnected, "Discord API disconnected (Zombied), retry attempts: {attempts}.", args.Failures);
            return Task.FromResult(true);
        }


#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async Task<int> DiscordPrefixResolver(DiscordMessage msg) {
            //var c = msg.Content; var trimmed = c.Length > 4 ? c.Substring(0, 4) : c;
            //logger.LogInformation(TLE.CmdDbg, $"Discord Prefix Resolver, {msg.Author.Username} : {trimmed}");

#if (ARCHIVAL_MODE)
            const string archivalPrefix = "ccArchive?";
            int mpos = msg.GetStringPrefixLength(archivalPrefix, StringComparison.OrdinalIgnoreCase);
            return mpos;
#else
            const string debugPrefix = "cc?";
            int mpos = msg.GetStringPrefixLength(debugPrefix, StringComparison.OrdinalIgnoreCase);
#endif

#if (DEBUG && !ARCHIVAL_MODE)
            // Get the prefix here, dont forget to have a default one.
            return mpos;// Task.FromResult(mpos);

#elif (ARCHIVAL_MODE == false)
            // If we are using the debugging prefix then we want to ignore this message in prod.
            if (mpos != -1) 
                return -1;

            // If direct message
            if (msg.Channel.IsPrivate) 
                return 0;

            var guildId = msg.Channel.Guild.Id;
            IEnumerable<string> prefixes = Program.ServerPrefixes.ContainsKey(guildId)
                ? Program.ServerPrefixes[guildId] : Program.g_DefaultPrefixes;

            foreach (var pfix in prefixes) {
                if (mpos == -1 && !string.IsNullOrWhiteSpace(pfix)) {
                    mpos = msg.GetStringPrefixLength(pfix, Program.Settings.PrefixesCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

                    if (mpos != -1)
                        break;
                }
            }

            return mpos;
#endif
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

    }
}