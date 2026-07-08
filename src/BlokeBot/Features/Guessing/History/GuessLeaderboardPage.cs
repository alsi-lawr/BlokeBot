namespace BlokeBot.Features.Guessing.History;

public sealed record GuessLeaderboardPage
{
    public int CorrectGuesses { get; init; }
    public IReadOnlyList<GuessLeaderboardEntry> Entries { get; init; } = [];
    public int Page { get; init; }
    public int PageCount =>
        TotalEntries == 0 ? 1 : (int)Math.Ceiling((double)TotalEntries / PageSize);
    public int PageSize { get; init; }
    public int TotalEntries { get; init; }
    public int TotalGuesses { get; init; }
    public int TotalPlayers { get; init; }
}
