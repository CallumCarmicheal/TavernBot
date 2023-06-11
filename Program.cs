using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using CCTavern;
using CCTavern.Commands;

using Config.Net;

using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CCTavern
{
    // We're sealing it because nothing will be inheriting this class
    public sealed class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        internal static ITavernSettings Settings { get; private set; }

        internal static Logger.TavernLoggerFactory LoggerFactory { get; private set; } = new CCTavern.Logger.TavernLoggerFactory();
        private static ILogger logger;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Remember to make your main method async! You no longer need to have both a Main and MainAsync method in the same class.
        public static async Task Main()
        {
            LoggerFactory = new CCTavern.Logger.TavernLoggerFactory();
            LoggerFactory.AddProvider(new CCTavern.Logger.TavernLoggerProvider());

            logger = LoggerFactory.CreateLogger<Program>();
            logger.LogInformation(LoggerEvents.Startup, "Application started.");

            // Load our settings
            Settings = new ConfigurationBuilder<ITavernSettings>()
                .UseAppConfig()
                .UseEnvironmentVariables()
                .UseJsonFile("./Configuration.json")
                .Build();

            // For the sake of examples, we're going to load our Discord token from an environment variable.
            if (string.IsNullOrWhiteSpace(Settings.DiscordToken))
            {
                Console.WriteLine("Please specify a token in the DISCORD_TOKEN environment variable.");
                Environment.Exit(1);

                // For the compiler's nullability, unreachable code.
                return;
            }

            // Next, we instantiate our client.
            DiscordConfiguration config = new()
            {
                Token = Settings.DiscordToken,
                TokenType = TokenType.Bot,

                // We're asking for unprivileged intents, which means we won't receive any member or presence updates.
                // Privileged intents must be enabled in the Discord Developer Portal.

                // TODO: Enable the message content intent in the Discord Developer Portal.
                // The !ping command will not work without it.
                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents 
                    | DiscordIntents.GuildVoiceStates | DiscordIntents.DirectMessageReactions
            };

            DiscordClient client = new(config);
            MusicBot music = new MusicBot(client);

            var services = new ServiceCollection();
            services.AddSingleton<MusicBot>(music);

            var commands = client.UseCommandsNext(new CommandsNextConfiguration() {
                StringPrefixes = new[] { "!!" },
                Services = services.BuildServiceProvider()
            });

            logger.LogInformation(LoggerEvents.Startup, "Registering commands");
            commands.RegisterCommands<MusicCmdModule>();

            // We can specify a status for our bot. Let's set it to "online" and set the activity to "with fire".
            DiscordActivity status = new("Loading :)", ActivityType.Playing);

            // Now we connect and log in.
            await client.ConnectAsync(status, UserStatus.DoNotDisturb);

            // Setup the lavalink connection
            
            await music.SetupLavalink();

            status = new("Ready", ActivityType.Playing);
            await client.UpdateStatusAsync(status, UserStatus.Online);

            // And now we wait infinitely so that our bot actually stays connected.
            await Task.Delay(-1);
        }
    }
}