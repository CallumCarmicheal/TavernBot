using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class TrackPlaylistPosition : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong Id { get; set; }

        public string TrackSource { get; set; }     // "youtube" etc.
        public string TrackSourceId { get; set; }   // source id, if youtube then the ?v={TrackSourceId}

        public TimeSpan Position { get; set; }      // Position of the track
        public string DisplayText { get; set; }
    }
}
