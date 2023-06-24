using DSharpPlus.Lavalink;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class GuildQueueItem : BaseDbEntity {
        [Key]
        public ulong Id { get; set; }

        public ulong Position { get; set; }

        public string Title { get; set; }

        public TimeSpan Length { get; set; }

        public string TrackString { get; set; }


        public bool IsDeleted { get; set; } = false;

        public DateTime DateDeleted { get; set; }

        [ForeignKey("Id")]
        public ulong? DeletedById { get; set; }
        public virtual CachedUser DeletedBy { get; set; }


        [ForeignKey("Id")]
        public ulong? RequestedById { get; set; }
        public virtual CachedUser RequestedBy { get; set; }

        [ForeignKey("Id")]
        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; }


        [ForeignKey("Id")]
        public ulong? PlaylistId { get; set; }
        public virtual GuildQueuePlaylist Playlist { get; set; }


    }
}
