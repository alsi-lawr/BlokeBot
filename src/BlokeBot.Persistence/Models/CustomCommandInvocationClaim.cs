namespace BlokeBot.Persistence.Models;

public sealed class CustomCommandInvocationClaim
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public int CustomCommandId { get; set; }

    public string? TwitchUserId { get; set; }

    public string? TwitchStreamId { get; set; }

    public DateTime ClaimedAtUtc { get; set; }

    public CustomCommand Command { get; set; } = null!;
}
