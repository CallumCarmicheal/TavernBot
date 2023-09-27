using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern {
    internal class GuildState {

        public ulong GuildId { get; set; }

        public ulong? TemporaryMusicChannelId { get; set; } = null;

        public bool ShuffleEnabled { get; set; } = false;

        public GuildState(ulong guildId) {
            this.GuildId = guildId;
        }

    }
}
