using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using CCTavern;
using CCTavern.Commands;
using CCTavern.Commands.Test;
using CCTavern.Database;
using CCTavern.Logger;

using Config.Net;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Identity.Client;

using Org.BouncyCastle.Asn1.Pkcs;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CCTavern
{
    // We're sealing it because nothing will be inheriting this class
    public sealed class Program
    {
        public static string DotNetConfigurationMode { get => get_VarDotNetConfigurationMode(); }


        internal static Logger.TavernLoggerFactory LoggerFactory { get; private set; } = new CCTavern.Logger.TavernLoggerFactory();
        
        internal static ITavernSettings Settings { get; private set; }

        public static Dictionary<ulong, IEnumerable<string>> ServerPrefixes = new Dictionary<ulong, IEnumerable<string>>();
        private static IEnumerable<string> g_DefaultPrefixes;

        private static ILogger logger;

        public static string VERSION_Full { get; private set; }
        public static string VERSION_Git { get; private set; } = "??";
        public static string VERSION_Git_WithBuild { get; private set; } = "??";

        private static DiscordClient client;

        // Remember to make your main method async! You no longer need to have both a Main and MainAsync method in the same class.
        public static async Task Main() 
        {
            loadVersionString();

            LoggerFactory = new CCTavern.Logger.TavernLoggerFactory();
            LoggerFactory.AddProvider(new CCTavern.Logger.TavernLoggerProvider());

            logger = LoggerFactory.CreateLogger<Program>();
            logger.LogInformation(TLE.Startup, "Application starting");

#if (ARCHIVAL_MODE)
            logger.LogInformation("!!! ARCHIVE MODE ONLY !!!");
#endif

            ReloadSettings();

            // For the sake of examples, we're going to load our Discord token from an environment variable.
            if (string.IsNullOrWhiteSpace(Settings.DiscordToken)) {
                Console.WriteLine("Please specify a token in the DISCORD_TOKEN environment variable.");
                Environment.Exit(1);
                return; // For the compiler's nullability, unreachable code.
            }

            // Setup default server prefixes
            var list = Settings.DefaultPrefixes.SplitWithTrim(Constants.PREFIX_SEPERATOR, '\\', true).ToList();
            g_DefaultPrefixes = list;

            // Setup database and load defaults
            await setupDatabase();

            // Next, we instantiate our client.
            DiscordConfiguration config = new() {
                Token = Settings.DiscordToken,
                TokenType = TokenType.Bot,

                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents 
                    | DiscordIntents.GuildVoiceStates | DiscordIntents.DirectMessageReactions
            };

            client = new(config);

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
                DmHelp = false,
                Services = services.BuildServiceProvider()
            });

            logger.LogInformation(TLE.Startup, "Registering commands");

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

            // Setup the lavalink connection
            await music.SetupLavalink();

            status = new("Ready", ActivityType.Playing);
            await client.UpdateStatusAsync(status, UserStatus.Online);

            logger.LogInformation(TLE.Startup, "Ready :)");

            // And now we wait infinitely so that our bot actually stays connected.
            await Task.Delay(-1);
        }

        private static void loadVersionString() {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            string? version = fileVersionInfo.ProductVersion;
            string gitHash = "??";

            if (version != null)
                gitHash = version?.Split("+")[1] ?? "";

            VERSION_Git = gitHash;
            VERSION_Full = version ?? "??";

#if DEBUG
            gitHash = "[DBG]#" + gitHash;
#else
            gitHash = "[REL]#" + gitHash; 
#endif

#if ARCHIVAL_MODE
            gitHash = "(ARCHIVAL):" + gitHash;
#endif

            VERSION_Git_WithBuild = gitHash;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<int> DiscordPrefixResolver(DiscordMessage msg) {
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
            IEnumerable<string> prefixes = ServerPrefixes.ContainsKey(guildId)
                ? ServerPrefixes[guildId] : g_DefaultPrefixes;

            foreach (var pfix in prefixes) {
                if (mpos == -1 && !string.IsNullOrWhiteSpace(pfix)) {
                    mpos = msg.GetStringPrefixLength(pfix, Settings.PrefixesCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

                    if (mpos != -1)
                        break;
                }
            }

            return mpos;
#endif
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        private static async Task setupDatabase() {
            logger.LogInformation(LoggerEvents.Startup, "Setting up database");

            var ctx = new TavernContext();

            var migrations = await ctx.Database.GetPendingMigrationsAsync();
            if (migrations.Any()) {
                logger.LogInformation(LoggerEvents.Startup, "Migrations required: " + string.Join(", ", migrations) + ".");
                await ctx.Database.MigrateAsync();
                await ctx.SaveChangesAsync();
            }

            ctx = new TavernContext();
            await ctx.Database.EnsureCreatedAsync();

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

        public static void ReloadSettings() {
            string jsonFile = "./Configuration.json";

            // Check if the configuration file exists
            if (System.IO.File.Exists("Configuration.json") == false) {
                // Attempt to load it from the bin folder assuming we are in the root of the source.

                var folder = "";
                var dnConfiguration = DotNetConfigurationMode;
                switch (dnConfiguration) {
                    case "Debug|Archival_Mode":
                        folder = "bin\\Archival_Debug\\net7.0\\";
                        break;
                    case "Release|Archival_Mode":
                        folder = "bin\\Archival_Debug\\net7.0\\";
                        break;
                    case "Debug":
                        folder = "bin\\Archival_Debug\\net7.0\\";
                        break;
                    case "Release":
                        folder = "bin\\Archival_Debug\\net7.0\\";
                        break;
                }

                jsonFile = Path.Join(Directory.GetCurrentDirectory(), folder, "Configuration.json");
            }

            Console.WriteLine("Configuration File: " + jsonFile);

            // Load our settings
            Settings = new ConfigurationBuilder<ITavernSettings>()
                .UseAppConfig()
                .UseEnvironmentVariables()
                .UseJsonFile(jsonFile)
                .Build();
        }

        private static string _dnMode = "";
        private static string get_VarDotNetConfigurationMode() {
            if (string.IsNullOrWhiteSpace(_dnMode) == false)
                return _dnMode;



#if (DEBUG && ARCHIVAL_MODE)
            // Archival_Debug
            _dnMode = "Debug|Archival_Mode";
#elif (ARCHIVAL_MODE)
            // Archival_Release
            _dnMode = "Release|Archival_Mode";
#elif (DEBUG)
            // Debug
            _dnMode = "Debug";
#elif (RELEASE)
            // Release / Other
            _dnMode = "Release";
#endif

            return _dnMode;
        }
    }
}
