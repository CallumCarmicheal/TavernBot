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
        /// Miscellaneous events, that do not fit in any other category.
        /// </summary>
        public static EventId Debug { get; } = new EventId(201, "Debug");

        /// <summary>
        /// Events pertaining to startup tasks.
        /// </summary>
        public static EventId Startup { get; } = new EventId(202, "Startup");

        /// <summary>
        /// Command debug
        /// </summary>
        public static EventId CmdDbg { get; } = new EventId(221, "CmdDbg");


        /// <summary>
        /// Miscellaneous events for text commands
        /// </summary>
        public static EventId MiscCommand { get; } = new EventId(220, "MiscCmd");



        /// <summary>
        /// Events pertaining to MusicBot Setup
        /// </summary>
        public static EventId MBSetup { get; } = new EventId(230, "MB:Setup");


        /// <summary>
        /// Events pertaining to MusicBot Joining a voice channel
        /// </summary>
        public static EventId MBJoin { get; } = new EventId(231, "MB:Join");

        /// <summary>
        /// Events pertaining to MusicBot Playing
        /// </summary>
        public static EventId MBPlay { get; } = new EventId(232, "MB:Play");

        /// <summary>
        /// Events pertaining to MusicBot Playing
        /// </summary>
        public static EventId MBFin { get; } = new EventId(233, "MB:Finish");

        /// <summary>
        /// Events pertaining to MusicBot Lavasink events
        /// </summary>
        public static EventId MBLava { get; } = new EventId(234, "MB:Lava");

    }
}
