using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Library.Lyrics;

public interface ILyricsProvider {
    // TODO LavaLyricsResponse to a generic payload later. 


    /// <summary>
    /// Gets timed lyrics for the track currently playing in the specified guild.
    ///
    /// Returns null when:
    /// - no lyrics could be found (204)
    /// - no track is currently playing (404)
    /// </summary>
    Task<LavaLyricsResponse?> GetCurrentLyricsAsync(
        string sessionId,
        ulong guildId,
        CancellationToken cancellationToken = default);
}
