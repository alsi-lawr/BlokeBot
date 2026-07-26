namespace BlokeBot.Persistence.Models;

public enum ShoutoutHistoryDirection
{
    Sent,
    Received,
}

public sealed class ShoutoutHistoryEntry
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public ShoutoutHistoryDirection Direction { get; set; }

    public string? ProviderMessageId { get; set; }

    public string SourceTwitchUserId { get; set; } = string.Empty;

    public string SourceLogin { get; set; } = string.Empty;

    public string TargetTwitchUserId { get; set; } = string.Empty;

    public string TargetLogin { get; set; } = string.Empty;

    public int ViewerCount { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public DateTime? CooldownEndsAtUtc { get; set; }

    public DateTime? TargetCooldownEndsAtUtc { get; set; }
}
