using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bingo;

public static class BingoCardLayout
{
    public static IReadOnlyList<BingoSquareKey> Generate(
        string seed,
        int templateRevision,
        BingoDimension dimension,
        string assignmentKey,
        IEnumerable<BingoSquareKey> squareKeys
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentKey);
        var keys = squareKeys.ToArray();
        _ =
            keys.Length == dimension.SquareCount && keys.Distinct().Count() == keys.Length
                ? keys
                : throw new ArgumentException(
                    "The revision must contain exactly one definition for every grid position.",
                    nameof(squareKeys)
                );

        return BingoIssuedLayout
            .Generate(
                seed,
                templateRevision,
                dimension.Value,
                assignmentKey,
                keys.Select(value => value.Value)
            )
            .Select(value => new BingoSquareKey(value))
            .ToArray();
    }

    public static IReadOnlyList<BingoWinLine> WinLines(
        BingoDimension dimension,
        bool includeFullCard
    )
    {
        var size = dimension.Value;
        var lines = new List<BingoWinLine>((size * 2) + 3);
        for (var row = 0; row < size; row++)
        {
            lines.Add(
                new(
                    BingoWinKind.Row,
                    row,
                    $"row:{row}",
                    Enumerable.Range(0, size).Select(column => (row * size) + column).ToArray()
                )
            );
        }
        for (var column = 0; column < size; column++)
        {
            lines.Add(
                new(
                    BingoWinKind.Column,
                    column,
                    $"column:{column}",
                    Enumerable.Range(0, size).Select(row => (row * size) + column).ToArray()
                )
            );
        }
        lines.Add(
            new(
                BingoWinKind.Diagonal,
                0,
                "diagonal:leading",
                Enumerable.Range(0, size).Select(index => (index * size) + index).ToArray()
            )
        );
        lines.Add(
            new(
                BingoWinKind.Diagonal,
                1,
                "diagonal:trailing",
                Enumerable
                    .Range(0, size)
                    .Select(index => (index * size) + (size - index - 1))
                    .ToArray()
            )
        );
        if (includeFullCard)
        {
            lines.Add(
                new(
                    BingoWinKind.FullCard,
                    0,
                    "full-card",
                    Enumerable.Range(0, dimension.SquareCount).ToArray()
                )
            );
        }
        return lines;
    }
}

public sealed record BingoWinLine(
    BingoWinKind Kind,
    int Index,
    string RuleKey,
    IReadOnlyList<int> Positions
);
