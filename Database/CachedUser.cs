using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class CachedUser : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong Id { get; set; }
        
        [Required]
        public ulong UserId { get; set; }

        [Required]
        public string Username { get; set; }
        
        [Required]
        public string DisplayName { get; set; }

        [ForeignKey("Id")]
        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; }


        public ICollection<GuildQueueItem> RequestedSongs { get; set; }
        public ICollection<GuildQueueItem> DeletedSongs { get; set; }
    }
}
