using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CCTavern.Commands;
using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;
using Config.Net;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Enums;
using DSharpPlus.Interactivity.Extensions;

using Lavalink4NET;
using Lavalink4NET.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Identity.Client;

using MySqlX.XDevAPI;

using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509.Qualified;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CCTavern
{


    public sealed class Program {
        public static string DotNetConfigurationMode { get => get_VarDotNetConfigurationMode(); }
        internal static ITavernSettings Settings { get; private set; }

        internal static Logger.TavernLoggerFactory LoggerFactory { get; private set; } = new CCTavern.Logger.TavernLoggerFactory();
        internal static Dictionary<ulong, IEnumerable<string>> ServerPrefixes = new Dictionary<ulong, IEnumerable<string>>();
        internal static IEnumerable<string> g_DefaultPrefixes;

        private static CancellationTokenSource applicationCancelTokenSource = new CancellationTokenSource();

        public static string VERSION_Full { get; private set; }
        public static string VERSION_Git { get; private set; } = "??";
        public static string VERSION_Git_WithBuild { get; private set; } = "??";

        private static ILogger logger;

        public static async Task Main(string[] args) {
            await SetupEnvironment();

            // Verify the discord token
            if (string.IsNullOrWhiteSpace(Settings.DiscordToken)) {
                Console.WriteLine("Please specify a token in the DISCORD_TOKEN environment variable.");
                Environment.Exit(1);
                return; // For the compiler's nullability, unreachable code.
            }

            // Setup default server prefixes
            var list = Settings.DefaultPrefixes.SplitWithTrim(Constants.PREFIX_SEPERATOR, '\\', true).ToList();
            g_DefaultPrefixes = list;

            // Next, we instantiate our client.
            DiscordConfiguration config = new() {
                Token = Settings.DiscordToken,
                TokenType = TokenType.Bot,

                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents
                          | DiscordIntents.GuildVoiceStates | DiscordIntents.DirectMessageReactions
            };

            var builder = new HostApplicationBuilder(args);
            builder.Services.AddSingleton<ILoggerFactory>(LoggerFactory);

            builder.Services.AddSingleton<ITavernSettings>(Settings);
            builder.Services.AddSingleton<DiscordConfiguration>(config);
            builder.Services.AddSingleton<DiscordClient>();
            builder.Services.AddSingleton<BotInactivityManager>();
            builder.Services.AddSingleton<BotInactivityImplementation>();
            builder.Services.AddSingleton<MusicBotHelper>();

            builder.Services.AddLavalink();
            builder.Services.ConfigureLavalink(config => {
                config.BaseAddress = new Uri($"http://{Settings.Lavalink.Hostname}:{Settings.Lavalink.Port}");
                config.Passphrase = Settings.Lavalink.Password;

                config.ReadyTimeout = TimeSpan.FromSeconds(10);
                config.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
            });

            builder.Services.AddHostedService<ApplicationHost>();

            // Logging
            builder.Services.AddLogging(s => s.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace));

            var services = builder.Build();
            var serviceProvider = services.Services;

            var webServer = new WebServer(serviceProvider);
            var webServerStartTask = webServer.StartServer(applicationCancelTokenSource.Token).ConfigureAwait(false);

            // Run the application
            services.RunAsync(applicationCancelTokenSource.Token).GetAwaiter().GetResult();
            //await builder.Build().RunAsync(applicationCancelTokenSource.Token);
        }

        public static void Shutdown() {
            applicationCancelTokenSource.Cancel();
        }

        public static async Task SetupEnvironment(bool exitOnError = true) {
            loadVersionString();
            ReloadSettings();

            LoggerFactory = new CCTavern.Logger.TavernLoggerFactory();
            LoggerFactory.AddProvider(new CCTavern.Logger.TavernLoggerProvider( Settings?.LogLevel ?? Microsoft.Extensions.Logging.LogLevel.Information ));

            logger = LoggerFactory.CreateLogger<Program>();
            logger.LogInformation(TLE.Startup, "Application starting, Version = {version}", VERSION_Full);

#if (ARCHIVAL_MODE)
            logger.LogInformation("!!! ARCHIVE MODE ONLY !!!");
#endif

            // Setup database and load defaults
            await setupDatabase();
        }

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

        public static void ReloadSettings() {
            string jsonFile = "./Configuration.json";

            // Check if the configuration file exists
            if (System.IO.File.Exists("Configuration.json") == false) {
                // Attempt to load it from the bin folder assuming we are in the root of the source.

                var folder = "";
                var dnConfiguration = DotNetConfigurationMode;
                switch (dnConfiguration) {
                    case "Debug|Archival_Mode":
                        folder = "bin\\Archival_Debug\\net8.0\\";
                        break;
                    case "Release|Archival_Mode":
                        folder = "bin\\Archival_Release\\net8.0\\";
                        break;
                    case "Debug":
                        folder = "bin\\Debug\\net8.0\\";
                        break;
                    case "Release":
                        folder = "bin\\Release\\net8.0\\";
                        break;
                }

                jsonFile = Path.Join(Directory.GetCurrentDirectory(), folder, "Configuration.json");
            }

            if (logger == null) {
                logger?.LogInformation("Loading/Reloading configuration file: {file}", jsonFile);
            } else {
                Console.WriteLine("Loading/Reloading configuration file: " + jsonFile);
            }

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
