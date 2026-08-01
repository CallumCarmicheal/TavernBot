namespace CCTavern.Database;

public sealed class GuildUser : BaseDbEntity {
    public ulong GuildId { get; set; }
    public Guild Guild { get; set; } = null!;

    public ulong UserId { get; set; }
    public TavernUser User { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;
}
