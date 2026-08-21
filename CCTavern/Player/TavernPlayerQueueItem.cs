using Lavalink4NET.Players;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Player {
    public class TavernPlayerQueueItem : ITrackQueueItem {
        public TrackReference Reference { get; set; }

        public string TrackAudioUrl { get; set; } = string.Empty;
        public string TrackUrl { get; set; } = string.Empty;
        public string TrackTitle { get; set; } = string.Empty;
        public string TrackThumbnail { get; set; } = string.Empty;

        public string? AuthorUrl { get; set; } = null;
        public string AuthorDisplayName { get; set; } = string.Empty;
        public string AuthorAvatarUrl { get; set; } = string.Empty;

        public string? AuthorSuffix { get; set; } = null; // If we want to suffix anything

        public T? As<T>() where T : class, ITrackQueueItem => this as T;
    }
}