using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using CCTavern;
using CCTavern.Commands;
using CCTavern.Commands.Test;
using CCTavern.Database;

using Config.Net;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using Org.BouncyCastle.Asn1.Pkcs;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CCTavern
{
    // We're sealing it because nothing will be inheriting this class
    public sealed class Program
    {
        internal static ITavernSettings Settings { get; private set; }

        internal static Logger.TavernLoggerFactory LoggerFactory { get; private set; } = new CCTavern.Logger.TavernLoggerFactory();

        public static Dictionary<ulong, IEnumerable<string>> ServerPrefixes = new Dictionary<ulong, IEnumerable<string>>();

        private static ILogger logger;

        // Remember to make your main method async! You no longer need to have both a Main and MainAsync method in the same class.
        public static async Task Main() 
        {
            LoggerFactory = new CCTavern.Logger.TavernLoggerFactory();
            LoggerFactory.AddProvider(new CCTavern.Logger.TavernLoggerProvider());

            logger = LoggerFactory.CreateLogger<Program>();
            logger.LogInformation(LoggerEvents.Startup, "Application starting");

            // Load our settings
            Settings = new ConfigurationBuilder<ITavernSettings>()
                .UseAppConfig()
                .UseEnvironmentVariables()
                .UseJsonFile("./Configuration.json")
                .Build();

            // For the sake of examples, we're going to load our Discord token from an environment variable.
            if (string.IsNullOrWhiteSpace(Settings.DiscordToken)) {
                Console.WriteLine("Please specify a token in the DISCORD_TOKEN environment variable.");
                Environment.Exit(1);
                return; // For the compiler's nullability, unreachable code.
            }

            setupDatabase();

            // Next, we instantiate our client.
            DiscordConfiguration config = new()
            {
                Token = Settings.DiscordToken,
                TokenType = TokenType.Bot,

                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents 
                    | DiscordIntents.GuildVoiceStates | DiscordIntents.DirectMessageReactions
            };

            DiscordClient client = new(config);

            client.UseInteractivity(new InteractivityConfiguration() {
                PollBehaviour = PollBehaviour.KeepEmojis,
                Timeout = TimeSpan.FromSeconds(30)
            });

            // We can specify a status for our bot. Let's set it to "online" and set the activity to "with fire".
            DiscordActivity status = new("Loading :)", ActivityType.Playing);

            // Now we connect and log in.
            await client.ConnectAsync(status, UserStatus.DoNotDisturb);

            MusicBot music = new MusicBot(client);

            var services = new ServiceCollection();
            services.AddSingleton(music);

            var commands = client.UseCommandsNext(new CommandsNextConfiguration() {
                CaseSensitive = Settings.PrefixesCaseSensitive,
                PrefixResolver = DiscordPrefixResolver,
                EnableMentionPrefix = true,
                DmHelp = true,
                Services = services.BuildServiceProvider()
            });

            logger.LogInformation(LoggerEvents.Startup, "Registering commands");

            commands.RegisterCommands<MusicCommandModule>();
            commands.RegisterCommands<MusicPlayModule>();
            commands.RegisterCommands<MusicQueueModule>();

            // Setup the lavalink connection
            await music.SetupLavalink();

            status = new("Ready", ActivityType.Playing);
            await client.UpdateStatusAsync(status, UserStatus.Online);

            // And now we wait infinitely so that our bot actually stays connected.
            await Task.Delay(-1);
        }

        private static Task<int> DiscordPrefixResolver(DiscordMessage msg) {
            const string debugPrefix = "cc?";
            int mpos = msg.GetStringPrefixLength(debugPrefix, StringComparison.OrdinalIgnoreCase);

#if (DEBUG)
            // Get the prefix here, dont forget to have a default one.
            return Task.FromResult(mpos);
#else
            // If we are using the debugging prefix then we want to ignore this message in prod.
            if (mpos != -1) 
                return Task.FromResult(-1);

            // If direct message
            if (msg.Channel.IsPrivate) 
                return Task.FromResult(0);

            var guildId = msg.Channel.Guild.Id;
            IEnumerable<string> prefixes = ServerPrefixes.ContainsKey(guildId)
                ? ServerPrefixes[guildId] : Settings.DefaultPrefixes;

            foreach (var pfix in prefixes) {
                if (mpos == -1 && !string.IsNullOrWhiteSpace(pfix)) {
                    mpos = msg.GetStringPrefixLength(pfix, Settings.PrefixesCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

                    if (mpos != -1)
                        break;
                }
            }

            return Task.FromResult(mpos);
#endif
        }

        private static void setupDatabase() {
            logger.LogInformation(LoggerEvents.Startup, "Setting up database");

            var ctx = new TavernContext();
            ctx.Database.EnsureCreated();

            // Load the server prefixes
            var prefixes = ctx.Guilds.Select(x => new { GuildId = x.Id, x.Prefixes }).ToList();

            foreach (var pfx in prefixes) {
                // Check if the prefix is not entered or is invalid.
                if (pfx.Prefixes == null || string.IsNullOrWhiteSpace(pfx.Prefixes)) 
                    continue;

                var list = pfx.Prefixes.SplitWithTrim(Constants.PREFIX_SEPERATOR, '\\', true).ToList();
                ServerPrefixes.Add(pfx.GuildId, list);
            }
        }
    }
}