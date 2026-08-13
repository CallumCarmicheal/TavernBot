using CCTavern.Database;
using CCTavern.Logger;
using CCTavern.Player;
using CCTavern.Web;

using Config.Net;

using DSharpPlus;

using Lavalink4NET;
using Lavalink4NET.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern {
    public sealed class Program {
        public static string DotNetConfigurationMode { get => get_VarDotNetConfigurationMode(); }
        internal static ITavernSettings Settings { get; private set; } = null!;
        internal static IDbContextFactory<TavernContext> DatabaseFactory { get; private set; } = null!;

        internal static Logger.TavernLoggerFactory LoggerFactory { get; private set; } = new CCTavern.Logger.TavernLoggerFactory();
        internal static Dictionary<ulong, IEnumerable<string>> ServerPrefixes = [];
        internal static IEnumerable<string> g_DefaultPrefixes = new List<string>(["!!"]); // When changing value also update Settings.DefaultPrefixes;

        private static readonly CancellationTokenSource applicationCancelTokenSource = new();

        public static string VERSION_Full { get; private set; } = "??";
        public static string VERSION_Git { get; private set; } = "??";
        public static string VERSION_Git_WithBuild { get; private set; } = "??";

        private static ILogger logger = null!;

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

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSingleton<ILoggerFactory>(LoggerFactory);

            builder.Services.AddSingleton<ITavernSettings>(Settings);
            builder.Services.AddSingleton<DiscordConfiguration>(config);
            builder.Services.AddSingleton<DiscordClient>();
            builder.Services.AddSingleton<BotInactivityManager>();
            builder.Services.AddSingleton<BotInactivityImplementation>();
            builder.Services.AddSingleton<MusicBotHelper>();
            builder.Services.AddSingleton<GuildEventWriter>();
            builder.Services.AddDbContextFactory<TavernContext>(options =>
                TavernContext.ConfigureOptions(options, Settings, LoggerFactory));

            builder.Services.AddLavalink();
            builder.Services.ConfigureLavalink(config => {
                config.BaseAddress = new Uri($"http://{Settings.Lavalink.Hostname}:{Settings.Lavalink.Port}");
                config.Passphrase = Settings.Lavalink.Password;
                config.ReadyTimeout = TimeSpan.FromSeconds(10);
                config.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
            });

            // Hosted services start sequentially in registration order. ApplicationHost does not
            // complete startup until Discord and Lavalink are ready, so Kestrel starts afterward.
            builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = false);
            builder.Services.AddHostedService<ApplicationHost>();
            WebServer.Configure(builder);

            // Logging
            builder.Services.AddLogging(s => s.AddConsole().SetMinimumLevel(LogLevel.Debug));

            WebApplication app;
            try {
                app = builder.Build();
            } catch (Microsoft.Extensions.Hosting.HostAbortedException) {
                // Entity Framework tooling deliberately aborts the host after discovering its services.
                throw;
            } catch (Exception ex) {
                logger.LogError(TLE.Startup, ex, $"Failed to build application host, waiting 5 seconds: {ex.Message}");
                await Task.Delay(5000);
                throw;
            }

            var serviceProvider = app.Services;
            DatabaseFactory = serviceProvider.GetRequiredService<IDbContextFactory<TavernContext>>();

            await setupDatabase(DatabaseFactory);
            WebServer.MapEndpoints(app);

            int shutdownDelayMilliseconds = (2 * 60) * 1000; // 2 minutes
            try {
                await ((IHost)app).RunAsync(applicationCancelTokenSource.Token).ConfigureAwait(false);

                logger.LogInformation("Host is running successfully.");
                return;
            } catch (OperationCanceledException) when (applicationCancelTokenSource.IsCancellationRequested) {
                // Application shutdown is expected to cancel the host and HTTP server.
                return;
            } catch (Exception ex) {
                logger.LogError(TLE.Startup, ex, $"Startup attempt failed: {ex.Message}");
                logger.LogInformation(TLE.Startup, $"Shutting down in {shutdownDelayMilliseconds / 1000} seconds...");
                Shutdown();
                await Task.Delay(shutdownDelayMilliseconds); // Wait 30 seconds before retrying
                return;
            }
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
        }

        private static async Task setupDatabase(IDbContextFactory<TavernContext> databaseFactory) {
            const string removeCachedUsersMigration = "20260731161550_RemoveCachedUsers";
            const string introduceUsersMigration = "20260731160315_IntroduceUsersAndGuildUsers";

            logger.LogInformation(LoggerEvents.Startup, "Setting up database");

            await using var ctx = await databaseFactory.CreateDbContextAsync();

            if (Settings?.DatabaseEngine != "mssql"
                    && ctx.GetService<IHistoryRepository>() is not MariaDbHistoryRepository) {
                throw new InvalidOperationException(
                    "The MariaDB-safe migration history repository was not registered.");
            }

            var databaseCreator = ctx.GetService<IRelationalDatabaseCreator>();
            var databaseExists = await databaseCreator.ExistsAsync();
            var isInitialDatabaseSeed = !databaseExists
                || !await databaseCreator.HasTablesAsync();

            var pendingMigrations = (await ctx.Database.GetPendingMigrationsAsync()).ToList();
            if (pendingMigrations.Count > 0) {
                logger.LogInformation(
                    LoggerEvents.Startup,
                    "Migrations required: {migrations}.",
                    string.Join(", ", pendingMigrations));

                if (pendingMigrations.Contains(removeCachedUsersMigration)
                        && !isInitialDatabaseSeed
                        && Settings?.ApplyDestructiveDatabaseMigrations != true) {
                    var migrationsBeforeContract = pendingMigrations
                        .TakeWhile(migration => migration != removeCachedUsersMigration)
                        .ToList();

                    logger.LogWarning(
                        LoggerEvents.Startup,
                        "The destructive {migration} migration is pending. Migrations will be applied only through {target}. "
                            + "Run validation and set applyDestructiveDatabaseMigrations=true when the contract migration is approved.",
                        removeCachedUsersMigration,
                        introduceUsersMigration);

                    if (migrationsBeforeContract.Count > 0) {
                        var migrator = ctx.GetService<IMigrator>();
                        await migrator.MigrateAsync(migrationsBeforeContract[^1]);
                    }
                } else {
                    if (isInitialDatabaseSeed
                            && pendingMigrations.Contains(removeCachedUsersMigration)) {
                        logger.LogInformation(
                            LoggerEvents.Startup,
                            "The database contains no tables. Applying the complete migration chain, including {migration}, during initial setup.",
                            removeCachedUsersMigration);
                    }

                    await ctx.Database.MigrateAsync();
                }
            }

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

            // Check if we have a version and it has a git hash code.
            if (version != null && version.Contains("+")) {
                var parts = version?.Split("+");
                gitHash = (parts != null && parts.Length >= 2) ? parts[1] : "";
            }

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
            if (System.IO.File.Exists(jsonFile) == false) {
                // Attempt to load it from the bin folder assuming we are in the root of the source.
                if (logger != null)
                     logger?.LogWarning("Failed to find json file in app directory {directory}, searching for development config.", Environment.CurrentDirectory);
                else Console.WriteLine($"Failed to find json file in app directory {Environment.CurrentDirectory}, searching for development config.");

                var folder = "";
                var dnConfiguration = DotNetConfigurationMode;
                switch (dnConfiguration) {
                    case "Debug|Archival_Mode":
                        folder = "bin\\Archival_Debug\\net10.0\\";
                        break;
                    case "Release|Archival_Mode":
                        folder = "bin\\Archival_Release\\net10.0\\";
                        break;
                    case "Debug":
                        folder = "bin\\Debug\\net10.0\\";
                        break;
                    case "Release":
                        folder = "bin\\Release\\net10.0\\";
                        break;
                }

                jsonFile = Path.Join(Directory.GetCurrentDirectory(), folder, "Configuration.json");
            }

            if (logger != null) 
                 logger?.LogInformation("Loading/Reloading configuration file: {file}", jsonFile);
            else Console.WriteLine("Loading/Reloading configuration file: " + jsonFile);

            var absoluteJsonFilePath = Path.GetFullPath(jsonFile);

            // Load our settings
            Settings = new ConfigurationBuilder<ITavernSettings>()
                .UseAppConfig()
                .UseEnvironmentVariables()
                .UseJsonFile(absoluteJsonFilePath)
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
