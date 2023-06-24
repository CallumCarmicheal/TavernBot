using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class Guild : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public ulong Id { get; set; }
        
        public string Name { get; set; }
        public string? Prefixes { get; set; } = null;

        public ulong? MusicChannelId { get; set; }
        public string? MusicChannelName { get; set; }

        public ulong? LastMessageStatusId { get; set; }

        public ulong NextTrack { get; set; } = 1;

        public ulong TrackCount { get; set; } = 0;

        public ulong CurrentTrack { get; set; }

        public bool IsPlaying { get; set; } = false;


        public ICollection<GuildQueueItem> Queue { get; set; }
    }
}
