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

        public string Title { get; set; }

        public int PlaylistSongCount { get; set; }

        public ulong? CreatedByUserId { get; set; }
        public virtual TavernUser? CreatedByUser { get; set; }

        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; } = null!;

        public ICollection<GuildQueueItem> Songs { get; set; } = [];
    }
}
