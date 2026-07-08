namespace BlokeBot.Features.Guessing.History;

public sealed record GuessLeaderboardEntry
{
    public int CorrectGuesses { get; init; }
    public double HitRate => RoundsPlayed == 0 ? 0 : (double)CorrectGuesses / RoundsPlayed;
    public DateTime LastGuessAtUtc { get; init; }
    public string Login { get; init; } = string.Empty;
    public int Rank { get; set; }
    public int RoundsPlayed { get; init; }
}
