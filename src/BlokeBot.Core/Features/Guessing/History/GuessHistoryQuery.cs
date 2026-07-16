namespace BlokeBot.Core.Features.Guessing.History;

public readonly record struct GuessHistoryQuery
{
    public DateTime? FromUtc { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int? ProfileId { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Username { get; init; }

    public GuessHistoryQuery() { }
}
