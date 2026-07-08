using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayMessageFormatter
{
    public PointOperationResult Reply(PointsGiveawayStartOutcome outcome) =>
        outcome.Kind switch
        {
            PointsGiveawayStartOutcomeKind.Started => Reply(
                true,
                outcome.Settings.GiveawayStartedReply,
                outcome.Settings
            ),
            PointsGiveawayStartOutcomeKind.AlreadyActive => Reply(
                false,
                outcome.Settings.GiveawayAlreadyActiveReply,
                outcome.Settings
            ),
            PointsGiveawayStartOutcomeKind.Cooldown => Reply(
                false,
                outcome.Settings.GiveawayCooldownReply,
                outcome.Settings,
                timeLeft: outcome.TimeLeft
            ),
            PointsGiveawayStartOutcomeKind.StreamOffline => Reply(
                false,
                outcome.Settings.StreamOfflineReply,
                outcome.Settings
            ),
            PointsGiveawayStartOutcomeKind.FollowerEligibilityUnavailable => Reply(
                false,
                outcome.Settings.FollowerEligibilityUnavailableReply,
                outcome.Settings
            ),
            _ => Reply(false, outcome.Settings.GiveawayNotActiveReply, outcome.Settings),
        };

    public PointOperationResult Reply(PointsGiveawayJoinOutcome outcome) =>
        outcome.Kind switch
        {
            PointsGiveawayJoinOutcomeKind.Joined => Reply(
                true,
                outcome.Settings.GiveawayJoinedReply,
                outcome.Settings,
                user: outcome.User
            ),
            PointsGiveawayJoinOutcomeKind.NotActive => Reply(
                false,
                outcome.Settings.GiveawayNotActiveReply,
                outcome.Settings
            ),
            PointsGiveawayJoinOutcomeKind.DuplicateJoin => Reply(
                false,
                outcome.Settings.GiveawayAlreadyJoinedReply,
                outcome.Settings,
                user: outcome.User
            ),
            PointsGiveawayJoinOutcomeKind.FollowerEligibilityUnavailable => Reply(
                false,
                outcome.Settings.FollowerEligibilityUnavailableReply,
                outcome.Settings
            ),
            PointsGiveawayJoinOutcomeKind.NotEligible => Reply(
                false,
                outcome.Settings.NotEligibleReply,
                outcome.Settings,
                user: outcome.User
            ),
            _ => Reply(false, outcome.Settings.GiveawayNotActiveReply, outcome.Settings),
        };

    public PointOperationResult Reply(PointsGiveawayDrawOutcome outcome) =>
        outcome.Kind switch
        {
            PointsGiveawayDrawOutcomeKind.Missing => new PointOperationResult(false, string.Empty),
            PointsGiveawayDrawOutcomeKind.NotActive when outcome.Settings is { } settings => Reply(
                false,
                settings.GiveawayNotActiveReply,
                settings
            ),
            PointsGiveawayDrawOutcomeKind.NoEntrants when outcome.Settings is { } settings => Reply(
                true,
                settings.GiveawayNoEntrantsReply,
                settings
            ),
            PointsGiveawayDrawOutcomeKind.Winners when outcome.Settings is { } settings => Reply(
                true,
                settings.GiveawayEndedReply,
                settings,
                winners: FormatWinners(outcome.Winners)
            ),
            _ => new PointOperationResult(false, string.Empty),
        };

    public PointOperationResult Reply(PointsGiveawayCancelOutcome outcome) =>
        outcome.Kind switch
        {
            PointsGiveawayCancelOutcomeKind.Cancelled => Reply(
                true,
                outcome.Settings.GiveawayCancelledReply,
                outcome.Settings
            ),
            PointsGiveawayCancelOutcomeKind.NotActive => Reply(
                false,
                outcome.Settings.GiveawayNotActiveReply,
                outcome.Settings
            ),
            _ => Reply(false, outcome.Settings.GiveawayNotActiveReply, outcome.Settings),
        };

    public PointOperationResult Reply(
        bool success,
        string template,
        PointsSettings settings,
        string? user = null,
        string? winners = null,
        TimeSpan? timeLeft = null
    ) =>
        new(
            success,
            Format(
                template,
                settings,
                user,
                winners,
                timeLeft is null ? null : FormatTimeLeft(timeLeft.Value)
            )
        );

    public string Format(
        string template,
        PointsSettings settings,
        string? user = null,
        string? winners = null,
        string? timeLeft = null
    ) =>
        MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = settings.PointLabel,
                ["user"] = user ?? string.Empty,
                ["winners"] = winners ?? string.Empty,
                ["time_left"] = timeLeft ?? string.Empty,
            }
        );

    public string FormatTimeLeft(TimeSpan timeLeft)
    {
        var seconds = Math.Max(0, (int)Math.Round(timeLeft.TotalSeconds));
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }

    private static string FormatWinners(IReadOnlyList<PointsGiveawayWinnerPayout> winners) =>
        string.Join(", ", winners.Select(x => $"{x.Login} ({x.Payout.ToDisplayString()})"));
}
