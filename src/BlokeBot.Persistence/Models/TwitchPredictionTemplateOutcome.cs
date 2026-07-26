namespace BlokeBot.Persistence.Models;

public sealed class TwitchPredictionTemplateOutcome
{
    public int Id { get; set; }
    public int TwitchPredictionTemplateId { get; set; }
    public TwitchPredictionTemplate Template { get; set; } = null!;
    public int Position { get; set; }
    public string Title { get; set; } = string.Empty;
}
