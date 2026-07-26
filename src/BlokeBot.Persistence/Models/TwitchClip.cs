namespace BlokeBot.Persistence.Models;

public sealed class TwitchClip
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public TwitchClipStatus Status { get; set; }

    public string? ProviderClipId { get; set; }

    public string? EditUrl { get; set; }

    public string? FinalUrl { get; set; }

    public string? BroadcasterTwitchUserId { get; set; }

    public string? BroadcasterLogin { get; set; }

    public string? CreatorTwitchUserId { get; set; }

    public string? CreatorLogin { get; set; }

    public string? VideoId { get; set; }

    public string? FailureReason { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public DateTime? LastCheckedAtUtc { get; set; }
}
