using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class GuildQueuePlaylist : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong Id { get; set; }

        public required string Title { get; set; }

        public int PlaylistSongCount { get; set; }

        [ForeignKey("Id")]
        public ulong? CreatedById { get; set; }
        public virtual CachedUser? CreatedBy { get; set; }

        public ICollection<GuildQueueItem>? Songs { get; set; }
    }
}
