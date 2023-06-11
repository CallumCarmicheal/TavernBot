using Config.Net;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern {
    public interface ITavernSettings {
        [Option(Alias = "discordToken", DefaultValue = "")]
        public string DiscordToken { get; }

        [Option(Alias = "lavalink")]
        public ILavalinkSettings Lavalink { get; set; }
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
}
