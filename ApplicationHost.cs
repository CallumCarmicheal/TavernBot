using CCTavern.Commands;
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
#endif

            // Initialize node connection
            await audioService
                .WaitForReadyAsync(stoppingToken)
                .ConfigureAwait(false);

            var date = DateTime.Now;
            status = new($"Ready ({date:dd/MM/yyyy HH:mm:ss})", ActivityType.Playing);
            await discordClient.UpdateStatusAsync(status, UserStatus.Online);

            logger.LogInformation(TLE.Startup, "Ready :)");

            //logger.LogInformation("Finished Loading!");
            // await _audioService.Players
            //     .JoinAsync(0, 0, playerOptions, stoppingToken) // Ids
            //     .ConfigureAwait(false);
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