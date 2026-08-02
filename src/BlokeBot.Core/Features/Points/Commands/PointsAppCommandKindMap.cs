using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Points.Commands;

public static class PointsAppCommandKindMap
{
    public static IReadOnlySet<AppCommandKind> AppKinds { get; } =
        new HashSet<AppCommandKind>
        {
            AppCommandKind.Points,
            AppCommandKind.GivePoints,
            AppCommandKind.AddPoints,
            AppCommandKind.RemovePoints,
            AppCommandKind.Gamble,
            AppCommandKind.Giveaway,
            AppCommandKind.Join,
            AppCommandKind.EndGiveaway,
            AppCommandKind.CancelGiveaway,
        };

    public static AppCommandKind ToAppKind(PointsCommandKind kind) =>
        kind switch
        {
            PointsCommandKind.Points => AppCommandKind.Points,
            PointsCommandKind.GivePoints => AppCommandKind.GivePoints,
            PointsCommandKind.AddPoints => AppCommandKind.AddPoints,
            PointsCommandKind.RemovePoints => AppCommandKind.RemovePoints,
            PointsCommandKind.Gamble => AppCommandKind.Gamble,
            PointsCommandKind.Giveaway => AppCommandKind.Giveaway,
            PointsCommandKind.Join => AppCommandKind.Join,
            PointsCommandKind.EndGiveaway => AppCommandKind.EndGiveaway,
            PointsCommandKind.CancelGiveaway => AppCommandKind.CancelGiveaway,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static Option<PointsCommandKind> FromAppKind(AppCommandKind appKind) =>
        appKind switch
        {
            AppCommandKind.Points => Option<PointsCommandKind>.Some(PointsCommandKind.Points),
            AppCommandKind.GivePoints => Option<PointsCommandKind>.Some(
                PointsCommandKind.GivePoints
            ),
            AppCommandKind.AddPoints => Option<PointsCommandKind>.Some(PointsCommandKind.AddPoints),
            AppCommandKind.RemovePoints => Option<PointsCommandKind>.Some(
                PointsCommandKind.RemovePoints
            ),
            AppCommandKind.Gamble => Option<PointsCommandKind>.Some(PointsCommandKind.Gamble),
            AppCommandKind.Giveaway => Option<PointsCommandKind>.Some(PointsCommandKind.Giveaway),
            AppCommandKind.Join => Option<PointsCommandKind>.Some(PointsCommandKind.Join),
            AppCommandKind.EndGiveaway => Option<PointsCommandKind>.Some(
                PointsCommandKind.EndGiveaway
            ),
            AppCommandKind.CancelGiveaway => Option<PointsCommandKind>.Some(
                PointsCommandKind.CancelGiveaway
            ),
            _ => Option<PointsCommandKind>.None,
        };
}
