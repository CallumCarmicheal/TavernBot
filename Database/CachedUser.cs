using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class CachedUser {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public ulong Id { get; set; }

        [Required]
        public string Username { get; set; }
        
        [Required]
        public string DisplayName { get; set; }
    }
}
