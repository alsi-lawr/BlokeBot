using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Commands;

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

    public static AppCommandKind ToAppKind(PointsCommandKind kind)
    {
        return kind switch
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
    }

    public static bool TryFromAppKind(AppCommandKind appKind, out PointsCommandKind kind)
    {
        kind = appKind switch
        {
            AppCommandKind.Points => PointsCommandKind.Points,
            AppCommandKind.GivePoints => PointsCommandKind.GivePoints,
            AppCommandKind.AddPoints => PointsCommandKind.AddPoints,
            AppCommandKind.RemovePoints => PointsCommandKind.RemovePoints,
            AppCommandKind.Gamble => PointsCommandKind.Gamble,
            AppCommandKind.Giveaway => PointsCommandKind.Giveaway,
            AppCommandKind.Join => PointsCommandKind.Join,
            AppCommandKind.EndGiveaway => PointsCommandKind.EndGiveaway,
            AppCommandKind.CancelGiveaway => PointsCommandKind.CancelGiveaway,
            _ => default,
        };
        return AppKinds.Contains(appKind);
    }
}
