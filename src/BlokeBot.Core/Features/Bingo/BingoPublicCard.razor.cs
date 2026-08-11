using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Bingo;

public partial class BingoPublicCard
{
    private IReadOnlyList<string> _letters = [];
    private IReadOnlyList<EvidenceEntry> _evidence = [];
    private IReadOnlySet<int> _winningPositions = new HashSet<int>();
    private string _evidenceLabel = string.Empty;
    private string _rewardSummary = string.Empty;
    private bool _evidenceOpen = true;

    [Parameter, EditorRequired]
    public BingoGameView Game { get; set; } = default!;

    [Parameter, EditorRequired]
    public BingoCardView Card { get; set; } = default!;

    /// <summary>
    /// The moderation control a dashboard puts on a square, drawn inside the cell so the correction
    /// happens where the viewer sees the result. Public rendering leaves it unset.
    /// </summary>
    [Parameter]
    public RenderFragment<BingoSquareView>? SquareAction { get; set; }

    /// <summary>
    /// Identifies the squares whose action fragment produces a control, allowing the cell to reserve
    /// room without changing public cards or squares that do not have an action.
    /// </summary>
    [Parameter]
    public Func<BingoSquareView, bool>? SquareActionVisible { get; set; }

    [Parameter]
    public string EvidenceTitle { get; set; } = "Public evidence";

    private string _foldKey => $"bingo-evidence-{Card.Id.Value:N}";

    protected override void OnParametersSet()
    {
        _letters = Game.Dimension.LetterRail();
        _winningPositions = BingoViewPresentation.WinningPositions(Game.Dimension, Card.Wins);
        _evidence =
        [
            .. Card
                .Squares.SelectMany(square =>
                    square.Evidence.Select(evidence => new EvidenceEntry(square, evidence))
                )
                .OrderByDescending(entry => entry.Evidence.OccurredAtUtc),
        ];
        _evidenceLabel =
            $"{EvidenceTitle} · {_evidence.Count} {(_evidence.Count == 1 ? "entry" : "entries")}";
        _rewardSummary = RewardSummary();
    }

    private string CellClass(BingoSquareView square)
    {
        var marked = square.Marked ? " bingo-cell--marked" : string.Empty;
        var win = _winningPositions.Contains(square.Position) ? " bingo-cell--win" : string.Empty;
        var action = HasSquareAction(square) ? " bingo-cell--has-action" : string.Empty;
        return $"bingo-cell{marked}{win}{action}";
    }

    private bool HasSquareAction(BingoSquareView square) =>
        SquareAction is not null && (SquareActionVisible?.Invoke(square) ?? true);

    private RenderFragment SquareActionFor(BingoSquareView square) => SquareAction!(square);

    private string RewardSummary()
    {
        var pending = Card.Wins.Any(win => win.RewardRecipients.Count > 0 && !win.RewardsCompleted);
        var recipients = Card
            .Wins.SelectMany(win => win.RewardRecipients)
            .Select(value => $"@{value.Login}")
            .Distinct()
            .ToArray();
        return recipients.Length == 0
            ? string.Empty
            : $"{(pending ? "Reward pending for" : "Rewarded")} {string.Join(", ", recipients)}";
    }

    private sealed record EvidenceEntry(BingoSquareView Square, BingoEvidenceView Evidence);
}
