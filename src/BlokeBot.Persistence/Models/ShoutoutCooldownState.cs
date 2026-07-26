namespace BlokeBot.Persistence.Models;

public sealed class ShoutoutCooldownState
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public DateTime? GlobalEligibleAtUtc { get; set; }

    public string? TargetTwitchUserId { get; set; }

    public string? TargetLogin { get; set; }

    public DateTime? TargetEligibleAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
