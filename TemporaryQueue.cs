using CCTavern.Database;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern {
    public class TemporaryQueue {
        public ulong GuildId { get; private set; }

        public TemporaryQueue (ulong guildId) {
            this.GuildId = guildId;
        }

        public ulong SongCount { get; set; } = 0;
        public bool  IsPlaying { get; set; } = false;

        public List<TemporarySongQueueItem> SongItems = new List<TemporarySongQueueItem>();

        #region Classes

        public abstract class TemporarySongQueueItem {
            public abstract GuildQueueItem GetQueueItem();
            public abstract void FinishedPlaying(TemporaryQueue tempQueue);
        }

        public class TemporarySong : TemporarySongQueueItem {
            public GuildQueueItem QueueItem { get; }

            public TemporarySong(GuildQueueItem qi) {
                this.QueueItem = qi;
            }

            public override GuildQueueItem GetQueueItem() {
                return QueueItem;
            }

            public override void FinishedPlaying(TemporaryQueue tempQueue) {
                if (tempQueue.SongItems == null) {
                    tempQueue.SongCount = 0;
                    return;
                }

                tempQueue.SongCount--;
                tempQueue.SongItems.Remove(this);

                if (tempQueue.SongCount < 0)
                    tempQueue.SongCount = 0;
            }
        }

        public class TemporaryPlaylist : TemporarySongQueueItem {
            public string Title { get; set; }

            public int PlaylistSongCount => Songs.Count;

            public ulong? CreatedById { get; set; }

            public List<GuildQueueItem> Songs { get; }

            public TemporaryPlaylist(string title, ulong? createdById, List<GuildQueueItem> songs) {
                Title = title;
                CreatedById = createdById;
                Songs = songs;
            }

            public override void FinishedPlaying(TemporaryQueue tempQueue) {
                if (Songs.Count == 0) {
                    if (tempQueue.SongItems == null) {
                        tempQueue.SongCount = 0;
                        return;
                    }

                    tempQueue.SongItems.Remove(this);
                }

                tempQueue.SongCount--;
                Songs.RemoveAt(0);

                if (tempQueue.SongCount < 0) 
                    tempQueue.SongCount = 0;

                if (Songs.Count == 0) {
                    if (tempQueue.SongItems == null) {
                        tempQueue.SongCount = 0;
                        return;
                    }

                    tempQueue.SongItems.Remove(this);
                }
            }

            public override GuildQueueItem GetQueueItem() {
                return Songs[0];
            }
        }

        #endregion
    }
}
