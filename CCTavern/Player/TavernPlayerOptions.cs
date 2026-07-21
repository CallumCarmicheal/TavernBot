using DSharpPlus.Entities;

using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Player {
    public record TavernPlayerOptions : LavalinkPlayerOptions {
        public ulong? VoiceChannelId { get; set; }
    }
}
