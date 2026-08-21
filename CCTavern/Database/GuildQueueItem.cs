using DSharpPlus;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Database {
    public class GuildQueueItem : BaseDbEntity {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public ulong Id { get; set; }

        public ulong GuildId { get; set; }
        public virtual Guild Guild { get; set; } = null!;


        public ulong Position { get; set; }

        public string Title { get; set; } = string.Empty;

        public TimeSpan Length { get; set; }

        public string TrackString { get; set; } = string.Empty;

        public bool IsDeleted { get; set; } = false;

        public DateTime? DateDeleted { get; set; }

        public ulong? DeletedByUserId { get; set; }
        public virtual TavernUser? DeletedByUser { get; set; } = null!;

        public string? DeletedReason { get; set; }

        public ulong? RequestedByUserId { get; set; }
        public virtual TavernUser? RequestedByUser { get; set; } = null!;


        public ulong? PlaylistId { get; set; }
        public virtual GuildQueuePlaylist Playlist { get; set; } = null!;


        public async Task<string> GetTagline(TavernContext? ctx = null, bool fetchUserIfNotPresent = false, CancellationToken cancellationToken = default) {

            if (ctx == null || RequestedByUser == null) {
                return ($"`{Title}` at position `{Position}`, requested at "
                      + $"`{CreatedAt:dd/MM/yyyy HH:mm:ss}` ({Formatter.Timestamp(CreatedAt, TimestampFormat.RelativeTime)})");
            }

            if (RequestedByUser != null) {
                //displayName = await ctx.GetUserDisplayNameForGuildAsync(RequestedByUser, GuildId, cancellationToken) ?? displayName;
                return $"`{Title}` at position `{Position}`, requested by `{RequestedByUser.Username}` at "
                    + $"`{CreatedAt:dd/MM/yyyy HH:mm:ss}` ({Formatter.Timestamp(CreatedAt, TimestampFormat.RelativeTime)})";
            }

            return $"`{Title}` at position `{Position}`, requested at "
                      + $"`{CreatedAt:dd/MM/yyyy HH:mm:ss}` ({Formatter.Timestamp(CreatedAt, TimestampFormat.RelativeTime)})";
        }
    }
}