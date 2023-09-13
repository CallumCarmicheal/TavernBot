using DSharpPlus;
using DSharpPlus.Lavalink;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class GuildQueueItem : BaseDbEntity {
        [Key]
        public ulong Id { get; set; }

        public ulong Position { get; set; }

        public string Title { get; set; }

        public TimeSpan Length { get; set; }

        public string TrackString { get; set; }


        public bool IsDeleted { get; set; } = false;

        public DateTime? DateDeleted { get; set; }

        [ForeignKey("Id")]
        public ulong? DeletedById { get; set; }
        public virtual CachedUser DeletedBy { get; set; }

        public string? DeletedReason { get; set; }

        [ForeignKey("Id")]
        public ulong? RequestedById { get; set; }
        public virtual CachedUser RequestedBy { get; set; }

        [ForeignKey("Id")]
        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; }

        [ForeignKey("Id")]
        public ulong? PlaylistId { get; set; }
        public virtual GuildQueuePlaylist Playlist { get; set; }



        public async Task<string> GetTagline(TavernContext? ctx = null, bool fetchUserIfNotPresent = false) {

            if (ctx == null && RequestedBy == null) {
                return ($"`{Title}` at position `{Position}`, requested at "
                    + $"`{CreatedAt:dd/MM/yyyy HH:mm:ss}` ({Formatter.Timestamp(CreatedAt, TimestampFormat.RelativeTime)})");
            }

            if (fetchUserIfNotPresent && RequestedBy == null) {
                var userQuery = ctx.CachedUsers.Where(x => x.UserId == RequestedById);
                if (userQuery.Any()) 
                    RequestedBy = await userQuery.FirstAsync();
            }

            if (RequestedBy == null) {
                return ($"`{Title}` at position `{Position}`, requested at "
                    + $"`{CreatedAt:dd/MM/yyyy HH:mm:ss}` ({Formatter.Timestamp(CreatedAt, TimestampFormat.RelativeTime)})");
            }

            return $"`{Title}` at position `{Position}`, requested by `{RequestedBy.Username}` at "
                +  $"`{CreatedAt:dd/MM/yyyy HH:mm:ss}` ({Formatter.Timestamp(CreatedAt, TimestampFormat.RelativeTime)})";
        }
    }
}
