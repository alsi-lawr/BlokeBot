namespace BlokeBot.Persistence.Models;

public sealed class TwitchPrediction
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string ProviderPredictionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OutcomesJson { get; set; } = "[]";
    public TwitchPredictionStatus Status { get; set; }
    public bool IsExternallyStarted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LocksAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
