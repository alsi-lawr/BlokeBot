namespace BlokeBot.Persistence.Models;

public sealed class TwitchPredictionTemplate
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PredictionWindowSeconds { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<TwitchPredictionTemplateOutcome> Outcomes { get; set; } = [];
}
