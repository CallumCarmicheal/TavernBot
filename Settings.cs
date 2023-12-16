using Config.Net;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern {
    public interface ITavernSettings {

        [Option(Alias = "defaultPrefixes", DefaultValue = "!!")]
        public string DefaultPrefixes { get; }

        [Option(Alias = "prefixesCaseSensitive", DefaultValue = false)]
        public bool PrefixesCaseSensitive { get; }


        [Option(Alias = "discordToken", DefaultValue = "")]
        public string DiscordToken { get; }

        [Option(Alias = "lavalink")]
        public ILavalinkSettings Lavalink { get; set; }


        [Option(Alias = "logDatabaseQueries", DefaultValue = false)]
        public bool LogDatabaseQueries { get; set; }

        [Option(Alias = "loggingVerbose", DefaultValue = false)]
        public bool LoggingVerbose { get; set; }

        [Option(Alias = "dbEngine", DefaultValue = "mysql")]
        public string DatabaseEngine { get; set; }

        [Option(Alias = "dbConnectionString", DefaultValue = "")]
        public string DatabaseConnectionString { get; set; }

        [Option(Alias = "debug")]
        public IDebugSettings ConnectionDebugging { get; set; }
    }

    // ll.hostname=127.0.0.1 ll.port=2800 ll.password=Password123
    public interface ILavalinkSettings {
        [Option(Alias = "hostname", DefaultValue = "127.0.0.1")]
        public string Hostname { get; set; }

        [Option(Alias = "port", DefaultValue = 8200)]
        public int Port { get; set; }

        [Option(Alias = "password", DefaultValue = "")]
        public string Password { get; set; }
    }

    public interface IDebugSettings {
        [Option(Alias = "discordChannelId", DefaultValue = null)]
        public ulong? DiscordChannelId { get; set; }
        [Option(Alias = "discordThreadId", DefaultValue = null)]
        public ulong? DiscordThreadId { get; set; }

        /// <summary>
        /// The id of the message to edit when ever the bot needs to update its login status.
        /// </summary>
        [Option(Alias = "discordMessageId", DefaultValue = null)]
        public ulong? DiscordMessageId { get; set; }
    }
}
