namespace BlokeBot.Persistence.Models;

public sealed class GuessRound
{
    public int Id { get; set; }
    public int GuessRoundProfileId { get; set; }
    public GuessRoundProfile? GuessRoundProfile { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? WinningName { get; set; }
    public List<GuessVote> Votes { get; set; } = [];
}
