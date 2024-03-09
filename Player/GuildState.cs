using DSharpPlus.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static CCTavern.Player.YoutubeChaptersParser;

namespace CCTavern.Player
{
    internal class GuildState
    {

        public ulong GuildId { get; set; }

        public ulong? TemporaryMusicChannelId { get; set; } = null;

        public bool ShuffleEnabled { get; set; } = false;
        public bool RepeatEnabled { get; internal set; } = false;
        public int TimesRepeated { get; set; } = 0;


        public MusicEmbedState? MusicEmbed { get; set; } = null;
        public SortedList<TimeSpan, IVideoChapter>? TrackChapters { get; set; }


        public GuildState(ulong guildId)
        {
            GuildId = guildId;
        }
    }

    internal class MusicEmbedState
    {
        //public ulong MessageId { get; set; } 

        public DiscordEmbedBuilder Embed { get; set; }

        public int FieldIndex { get; set; }

        // public DateTime NextUpdate { get; set; }

        public DiscordMessage Message { get; set; }
    }
}
