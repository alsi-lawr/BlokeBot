using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bingo;

internal static class BingoViewPresentation
{
    private static readonly string[] _letters = ["B", "I", "N", "G", "O"];

    internal static string Stamp(this DateTime value) =>
        FormattableString.Invariant($"{value:HH:mm} UTC · {value.Day} {value:MMM}");

    internal static string DayStamp(this DateTime value) =>
        FormattableString.Invariant($"{value.Day} {value:MMM}");

    internal static string PillClass(this BingoGameStatus value) =>
        value switch
        {
            BingoGameStatus.Joining => "status-pill status-pill--blue",
            BingoGameStatus.Issued or BingoGameStatus.Completed => "status-pill status-pill--green",
            _ => "status-pill status-pill--slate",
        };

    internal static string Label(this BingoGameStatus value) =>
        value switch
        {
            BingoGameStatus.Issued => "Live",
            _ => value.ToString(),
        };

    internal static string Label(this BingoGameMode value) =>
        value switch
        {
            BingoGameMode.Shared => "Shared",
            BingoGameMode.UniquePerViewer => "Per viewer",
            BingoGameMode.Team => "Teams",
            _ => value.ToString(),
        };

    internal static string Label(this BingoWinKind value) =>
        value switch
        {
            BingoWinKind.Row => "Row win",
            BingoWinKind.Column => "Column win",
            BingoWinKind.Diagonal => "Diagonal win",
            BingoWinKind.FullCard => "Full card",
            _ => value.ToString(),
        };

    internal static string Label(this BingoEvidenceAction value) =>
        value switch
        {
            BingoEvidenceAction.Reversed => "Reversed",
            _ => "Marked",
        };

    internal static string DisclosureKey(this BingoGameView value) =>
        FormattableString.Invariant($"game-{value.Id.Value:N}");

    internal static int WinCount(this BingoGameView value) =>
        value.Cards.Sum(card => card.Wins.Count);

    internal static string WinDetail(this BingoGameView value)
    {
        var latest = value
            .Cards.SelectMany(card => card.Wins.Select(win => new { Card = card, Win = win }))
            .OrderByDescending(entry => entry.Win.CompletedAtUtc)
            .FirstOrDefault();
        return latest is null
            ? "none yet"
            : $"{latest.Win.Kind.Label()} · {latest.Card.AssignmentName}";
    }

    internal static string GridVariable(this BingoDimension value) =>
        FormattableString.Invariant($"--bingo-dimension: {value.Value}");

    internal static string GridLabel(this BingoDimension value) =>
        FormattableString.Invariant($"{value.Value}×{value.Value}");

    internal static IReadOnlyList<string> LetterRail(this BingoDimension value) =>
        value.Value == 5 ? _letters : [];

    /// <summary>
    /// The card positions covered by the recorded wins, so a completed line can carry the gold ring.
    /// The geometry is recomputed from the same rule keys the service recorded the win under.
    /// </summary>
    internal static IReadOnlySet<int> WinningPositions(
        BingoDimension dimension,
        IReadOnlyList<BingoWinView> wins
    )
    {
        if (wins.Count == 0)
        {
            return new HashSet<int>();
        }

        var lines = BingoCardLayout
            .WinLines(dimension, true)
            .ToDictionary(value => value.RuleKey, value => value.Positions);
        var positions = new HashSet<int>();
        foreach (var win in wins)
        {
            if (lines.TryGetValue(win.RuleKey, out var line))
            {
                positions.UnionWith(line);
            }
        }
        return positions;
    }
}
