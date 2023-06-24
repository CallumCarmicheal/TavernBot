using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Logger {
    /// <summary>
    /// Tavern Logger Events
    /// </summary>
    public static class TLE {
        /// <summary>
        /// Miscellaneous events, that do not fit in any other category.
        /// </summary>
        public static EventId Misc { get; } = new EventId(200, "CCTavern");

        /// <summary>
        /// Events pertaining to startup tasks.
        /// </summary>
        public static EventId Startup { get; } = new EventId(201, "Startup");

        /// <summary>
        /// Command debug
        /// </summary>
        public static EventId CmdDbg { get; } = new EventId(202, "CmdDbg");


        /// <summary>
        /// Miscellaneous events for text commands
        /// </summary>
        public static EventId MiscCommand { get; } = new EventId(202, "MiscCmd");



        /// <summary>
        /// Events pertaining to MusicBot Setup
        /// </summary>
        public static EventId MBSetup { get; } = new EventId(210, "MB:Setup");


        /// <summary>
        /// Events pertaining to MusicBot Joining a voice channel
        /// </summary>
        public static EventId MBJoin { get; } = new EventId(211, "MB:Join");

        /// <summary>
        /// Events pertaining to MusicBot Playing
        /// </summary>
        public static EventId MBPlay { get; } = new EventId(212, "MB:Play");

        /// <summary>
        /// Events pertaining to MusicBot Lavasink events
        /// </summary>
        public static EventId MBLava { get; } = new EventId(213, "MB:Lava");

    }
}
