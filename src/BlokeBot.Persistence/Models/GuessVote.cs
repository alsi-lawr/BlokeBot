namespace BlokeBot.Persistence.Models;

public sealed class GuessVote
{
    public int Id { get; set; }
    public int GuessRoundId { get; set; }
    public GuessRound? GuessRound { get; set; }
    public string Login { get; set; } = string.Empty;
    public string GuessName { get; set; } = string.Empty;
    public DateTime GuessedAtUtc { get; set; }
}
