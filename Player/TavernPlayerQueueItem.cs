using Lavalink4NET.Players;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Player {
    public class TavernPlayerQueueItem : ITrackQueueItem {
        public TrackReference Reference { get; set; }

        public string TrackUrl { get; set; }
        public string TrackTitle { get; set; }
        public string TrackThumbnail { get; set; }
        public string AuthorDisplayName { get; set; }
        public string AuthorAvatarUrl { get; set; }
        public string TrackAudioUrl { get; set; }

        public T? As<T>() where T : class, ITrackQueueItem => this as T;
    }
}
