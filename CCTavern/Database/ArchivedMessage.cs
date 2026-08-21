using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class ArchivedMessage : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong Id { get; set; }

        public ulong MessageId { get; set; }

        public DateTime DateMessageCreated { get; set; }

        public ulong? AuthorUserId { get; set; }
        public virtual TavernUser? AuthorUser { get; set; }

        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; } = null!;

        public bool ContainsPrefix { get; set; }

        public string MessageContents { get; set; } = string.Empty;
    }
}