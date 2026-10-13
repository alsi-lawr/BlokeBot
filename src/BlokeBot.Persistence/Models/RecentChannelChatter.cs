namespace BlokeBot.Persistence.Models;

public sealed class RecentChannelChatter
{
    public int HostId { get; set; }
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; }
}
