namespace BlokeBot.Persistence.Models;

public sealed class TwitchStreamMarker
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public TwitchStreamMarkerStatus Status { get; set; }

    public string? ProviderMarkerId { get; set; }

    public string Description { get; set; } = string.Empty;

    public int PositionSeconds { get; set; }

    public string? MarkerUrl { get; set; }

    public string? VideoId { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public DateTime? EnrichedAtUtc { get; set; }
}
