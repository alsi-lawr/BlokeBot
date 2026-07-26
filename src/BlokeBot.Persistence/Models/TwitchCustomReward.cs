namespace BlokeBot.Persistence.Models;

public sealed class TwitchCustomReward
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string ProviderRewardId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Prompt { get; set; }
    public int Cost { get; set; }
    public bool IsManageable { get; set; }
    public bool IsPaused { get; set; }
    public bool IsUserInputRequired { get; set; }
    public bool IsMaxPerStreamEnabled { get; set; }
    public int? MaxPerStream { get; set; }
    public bool IsMaxPerUserPerStreamEnabled { get; set; }
    public int? MaxPerUserPerStream { get; set; }
    public bool IsGlobalCooldownEnabled { get; set; }
    public int? GlobalCooldownSeconds { get; set; }
    public bool ShouldRedemptionsSkipRequestQueue { get; set; }
    public string? BackgroundColor { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
