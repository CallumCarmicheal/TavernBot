using Lavalink4NET.Players;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Player {
    internal record RadioStreamPlayerOptions : LavalinkPlayerOptions {

        public string StreamUrl { get; set; }
    }
}
