using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayMessageFormatter
{
    private const string _streamLivenessUnavailableReply =
        "Stream status could not be checked right now.";
    private const string _invalidConfigurationReply = "Giveaway is unavailable.";
    private const string _payoutFailedReply = "Giveaway prizes could not be awarded.";

    public PointOperationOutcome Reply(
        PointsGiveawayStartOutcome outcome,
        ReplyDeliveryMap delivery
    )
    {
        return outcome.Match<PointOperationOutcome>(
            started =>
                Succeeded(FormatPlain(started.Settings.GiveawayStartedReply, started.Settings)),
            invalidConfiguration =>
                Failed(
                    $"{_invalidConfigurationReply} {invalidConfiguration.Failure.Message}",
                    CommandResponseTarget.Chat
                ),
            alreadyActive =>
                Failed(
                    FormatPlain(
                        alreadyActive.Settings.GiveawayAlreadyActiveReply,
                        alreadyActive.Settings
                    ),
                    delivery.TargetFor(PointsReplyKeys.GiveawayAlreadyActive)
                ),
            cooldown =>
                Failed(
                    FormatTimeLeft(
                        cooldown.Settings.GiveawayCooldownReply,
                        cooldown.Settings,
                        cooldown.TimeLeft
                    ),
                    delivery.TargetFor(PointsReplyKeys.GiveawayCooldown)
                ),
            streamOffline =>
                Failed(
                    FormatPlain(streamOffline.Settings.StreamOfflineReply, streamOffline.Settings),
                    delivery.TargetFor(PointsReplyKeys.StreamOffline)
                ),
            _ => Failed(_streamLivenessUnavailableReply, CommandResponseTarget.Chat),
            followerUnavailable =>
                Failed(
                    FormatPlain(
                        followerUnavailable.Settings.FollowerEligibilityUnavailableReply,
                        followerUnavailable.Settings
                    ),
                    delivery.TargetFor(PointsReplyKeys.FollowerEligibilityUnavailable)
                )
        );
    }

    public PointOperationOutcome Reply(PointsGiveawayJoinOutcome outcome, ReplyDeliveryMap delivery)
    {
        return outcome.Match<PointOperationOutcome>(
            joined =>
                Succeeded(
                    FormatUser(joined.Settings.GiveawayJoinedReply, joined.Settings, joined.User),
                    delivery.TargetFor(PointsReplyKeys.GiveawayJoined)
                ),
            notActive =>
                Failed(
                    FormatPlain(notActive.Settings.GiveawayNotActiveReply, notActive.Settings),
                    delivery.TargetFor(PointsReplyKeys.GiveawayNotActive)
                ),
            duplicate =>
                Failed(
                    FormatUser(
                        duplicate.Settings.GiveawayAlreadyJoinedReply,
                        duplicate.Settings,
                        duplicate.User
                    ),
                    delivery.TargetFor(PointsReplyKeys.GiveawayAlreadyJoined)
                ),
            followerUnavailable =>
                Failed(
                    FormatPlain(
                        followerUnavailable.Settings.FollowerEligibilityUnavailableReply,
                        followerUnavailable.Settings
                    ),
                    delivery.TargetFor(PointsReplyKeys.FollowerEligibilityUnavailable)
                ),
            notEligible =>
                Failed(
                    FormatUser(
                        notEligible.Settings.NotEligibleReply,
                        notEligible.Settings,
                        notEligible.User
                    ),
                    delivery.TargetFor(PointsReplyKeys.NotEligible)
                )
        );
    }

    public PointOperationOutcome Reply(PointsGiveawayDrawOutcome outcome, ReplyDeliveryMap delivery)
    {
        return outcome.Match<PointOperationOutcome>(
            _ => Failed(string.Empty, CommandResponseTarget.Chat),
            notActive =>
                Failed(
                    FormatPlain(notActive.Settings.GiveawayNotActiveReply, notActive.Settings),
                    delivery.TargetFor(PointsReplyKeys.GiveawayNotActive)
                ),
            noEntrants =>
                Succeeded(
                    FormatPlain(noEntrants.Settings.GiveawayNoEntrantsReply, noEntrants.Settings)
                ),
            _ => Failed(_payoutFailedReply, CommandResponseTarget.Chat),
            winners =>
                Succeeded(
                    FormatWinners(
                        winners.Settings.GiveawayEndedReply,
                        winners.Settings,
                        winners.Payouts
                    )
                )
        );
    }

    public PointOperationOutcome Reply(
        PointsGiveawayCancelOutcome outcome,
        ReplyDeliveryMap delivery
    )
    {
        return outcome.Match<PointOperationOutcome>(
            cancelled =>
                Succeeded(
                    FormatPlain(cancelled.Settings.GiveawayCancelledReply, cancelled.Settings)
                ),
            notActive =>
                Failed(
                    FormatPlain(notActive.Settings.GiveawayNotActiveReply, notActive.Settings),
                    delivery.TargetFor(PointsReplyKeys.GiveawayNotActive)
                )
        );
    }

    public string FormatUpdate(string template, PointsSettings settings, TimeSpan timeLeft)
    {
        return FormatTimeLeft(template, settings, timeLeft);
    }

    private static PointOperationOutcome Succeeded(
        string message,
        CommandResponseTarget target = CommandResponseTarget.Chat
    )
    {
        return new PointOperationOutcome.Succeeded(message, target);
    }

    private static PointOperationOutcome Failed(string message, CommandResponseTarget target)
    {
        return new PointOperationOutcome.Failed(message, target);
    }

    private static string FormatPlain(string template, PointsSettings settings)
    {
        return Format(template, settings, string.Empty, string.Empty, string.Empty);
    }

    private static string FormatUser(string template, PointsSettings settings, string user)
    {
        return Format(template, settings, user, string.Empty, string.Empty);
    }

    private static string FormatWinners(
        string template,
        PointsSettings settings,
        IReadOnlyList<PointsGiveawayWinnerPayout> winners
    )
    {
        var winnerText = string.Join(
            ", ",
            winners.Select(winner => $"{winner.Login} ({winner.Payout.ToDisplayString()})")
        );
        return Format(template, settings, string.Empty, winnerText, string.Empty);
    }

    private static string FormatTimeLeft(
        string template,
        PointsSettings settings,
        TimeSpan timeLeft
    )
    {
        return Format(template, settings, string.Empty, string.Empty, DescribeTimeLeft(timeLeft));
    }

    private static string Format(
        string template,
        PointsSettings settings,
        string user,
        string winners,
        string timeLeft
    )
    {
        return MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = settings.PointLabel,
                ["user"] = user,
                ["winners"] = winners,
                ["time_left"] = timeLeft,
            }
        );
    }

    private static string DescribeTimeLeft(TimeSpan timeLeft)
    {
        var seconds = Math.Max(0, (int)Math.Round(timeLeft.TotalSeconds));
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
