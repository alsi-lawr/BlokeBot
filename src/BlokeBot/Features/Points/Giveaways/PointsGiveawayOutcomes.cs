using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Giveaways;

public enum PointsGiveawayStartOutcomeKind
{
    Started,
    AlreadyActive,
    Cooldown,
    StreamOffline,
    FollowerEligibilityUnavailable,
}

public sealed record PointsGiveawayStartOutcome(
    PointsGiveawayStartOutcomeKind Kind,
    PointsSettings Settings,
    TimeSpan? TimeLeft = null
)
{
    public bool Success => Kind == PointsGiveawayStartOutcomeKind.Started;
}

public enum PointsGiveawayJoinOutcomeKind
{
    Joined,
    NotActive,
    DuplicateJoin,
    FollowerEligibilityUnavailable,
    NotEligible,
}

public sealed record PointsGiveawayJoinOutcome(
    PointsGiveawayJoinOutcomeKind Kind,
    PointsSettings Settings,
    string User
)
{
    public bool Success => Kind == PointsGiveawayJoinOutcomeKind.Joined;
}

public enum PointsGiveawayDrawOutcomeKind
{
    Missing,
    NotActive,
    NoEntrants,
    Winners,
}

public sealed record PointsGiveawayWinnerPayout(string Login, PointAmount Payout);

public sealed record PointsGiveawayDrawOutcome(
    PointsGiveawayDrawOutcomeKind Kind,
    PointsSettings? Settings,
    IReadOnlyList<PointsGiveawayWinnerPayout> Winners
)
{
    public bool Success =>
        Kind is PointsGiveawayDrawOutcomeKind.NoEntrants or PointsGiveawayDrawOutcomeKind.Winners;

    public static PointsGiveawayDrawOutcome Missing() =>
        new(PointsGiveawayDrawOutcomeKind.Missing, null, []);

    public static PointsGiveawayDrawOutcome NotActive(PointsSettings settings) =>
        new(PointsGiveawayDrawOutcomeKind.NotActive, settings, []);

    public static PointsGiveawayDrawOutcome NoEntrants(PointsSettings settings) =>
        new(PointsGiveawayDrawOutcomeKind.NoEntrants, settings, []);

    public static PointsGiveawayDrawOutcome WithWinners(
        PointsSettings settings,
        IReadOnlyList<PointsGiveawayWinnerPayout> winners
    ) => new(PointsGiveawayDrawOutcomeKind.Winners, settings, winners);
}

public enum PointsGiveawayCancelOutcomeKind
{
    Cancelled,
    NotActive,
}

public sealed record PointsGiveawayCancelOutcome(
    PointsGiveawayCancelOutcomeKind Kind,
    PointsSettings Settings
)
{
    public bool Success => Kind == PointsGiveawayCancelOutcomeKind.Cancelled;
}
