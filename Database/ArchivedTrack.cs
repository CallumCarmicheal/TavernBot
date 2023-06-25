using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class ArchivedTrack : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong Id { get; set; }

        public ulong MessageId { get; set; }

        public DateTime DateMessageCreated { get; set; } 

        #region GuildQueueItem

        public ulong Position { get; set; }

        public string Title { get; set; }

        public TimeSpan Length { get; set; }

        public string TrackString { get; set; }

        [ForeignKey("Id")]
        public ulong? RequestedById { get; set; }
        public virtual CachedUser RequestedBy { get; set; }

        [ForeignKey("Id")]
        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; }

        #endregion

    }
}
