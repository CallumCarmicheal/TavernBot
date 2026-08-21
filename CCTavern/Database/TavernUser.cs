using Microsoft.EntityFrameworkCore;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Database;

public sealed class TavernUser : BaseDbEntity {
    public ulong Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public ICollection<GuildUser> GuildUsers { get; set; } = [];
    public ICollection<GuildQueueItem> RequestedSongs { get; set; } = [];
    public ICollection<GuildQueueItem> DeletedSongs { get; set; } = [];
    public ICollection<GuildQueuePlaylist> CreatedPlaylists { get; set; } = [];
    public ICollection<ArchivedTrack> RequestedArchivedTracks { get; set; } = [];
    public ICollection<ArchivedMessage> AuthoredArchivedMessages { get; set; } = [];
}