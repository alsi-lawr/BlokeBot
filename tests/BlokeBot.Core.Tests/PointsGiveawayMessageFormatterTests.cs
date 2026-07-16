using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PointsGiveawayMessageFormatterTests
{
    private readonly PointsGiveawayMessageFormatter _formatter = new();
    private readonly PointsSettings _settings = new() { HostId = 7 };

    [Test]
    public void EveryStartOutcome_Formatting_PreservesExactTextAndTarget()
    {
        var delivery = WhisperDelivery(
            PointsReplyKeys.GiveawayAlreadyActive,
            PointsReplyKeys.GiveawayCooldown,
            PointsReplyKeys.StreamOffline,
            PointsReplyKeys.FollowerEligibilityUnavailable
        );
        var unavailable = new HostStreamLivenessOutcome.Unavailable(
            HostStreamLivenessUnavailableReason.ProviderRequestFailed,
            new HttpRequestException("provider failure")
        );

        AssertSucceeded(
            _formatter.Reply(new PointsGiveawayStartOutcome.Started(_settings), delivery),
            "Giveaway started. Type !join to enter.",
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayStartOutcome.InvalidConfiguration(
                    _settings,
                    new PointsConfigurationValidationError.GiveawayDurationBelowMinimum()
                ),
                delivery
            ),
            "Giveaway is unavailable. Giveaway entry time must be at least 1 second.",
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(new PointsGiveawayStartOutcome.AlreadyActive(_settings), delivery),
            "A giveaway is already active.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayStartOutcome.Cooldown(_settings, TimeSpan.FromSeconds(2)),
                delivery
            ),
            "Giveaways are on cooldown. Try again in 2 seconds.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(new PointsGiveawayStartOutcome.StreamOffline(_settings), delivery),
            "Giveaways can only start while the stream is live.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayStartOutcome.StreamLivenessUnavailable(_settings, unavailable),
                delivery
            ),
            "Stream status could not be checked right now.",
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayStartOutcome.FollowerEligibilityUnavailable(_settings),
                delivery
            ),
            "Follower eligibility is not available for this channel.",
            CommandResponseTarget.Whisper
        );
    }

    [Test]
    public void EveryJoinOutcome_Formatting_PreservesExactTextAndTarget()
    {
        var delivery = WhisperDelivery(
            PointsReplyKeys.GiveawayJoined,
            PointsReplyKeys.GiveawayNotActive,
            PointsReplyKeys.GiveawayAlreadyJoined,
            PointsReplyKeys.FollowerEligibilityUnavailable,
            PointsReplyKeys.NotEligible
        );

        AssertSucceeded(
            _formatter.Reply(new PointsGiveawayJoinOutcome.Joined(_settings, "viewer"), delivery),
            "viewer entered the giveaway.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayJoinOutcome.NotActive(_settings, "viewer"),
                delivery
            ),
            "No giveaway is active.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayJoinOutcome.DuplicateJoin(_settings, "viewer"),
                delivery
            ),
            "viewer, you are already entered.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayJoinOutcome.FollowerEligibilityUnavailable(_settings, "viewer"),
                delivery
            ),
            "Follower eligibility is not available for this channel.",
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayJoinOutcome.NotEligible(_settings, "viewer"),
                delivery
            ),
            "viewer, you are not eligible for this giveaway.",
            CommandResponseTarget.Whisper
        );
    }

    [Test]
    public void EveryDrawOutcome_Formatting_PreservesExactTextAndTarget()
    {
        var delivery = WhisperDelivery(PointsReplyKeys.GiveawayNotActive);

        AssertFailed(
            _formatter.Reply(new PointsGiveawayDrawOutcome.Missing(), delivery),
            string.Empty,
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(new PointsGiveawayDrawOutcome.NotActive(_settings), delivery),
            "No giveaway is active.",
            CommandResponseTarget.Whisper
        );
        AssertSucceeded(
            _formatter.Reply(new PointsGiveawayDrawOutcome.NoEntrants(_settings), delivery),
            "Giveaway ended with no eligible entrants.",
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayDrawOutcome.PayoutFailed(
                    _settings,
                    new PointBalanceMutationFailure.CapExceeded(
                        PointAmount.ParseAbsolute("100"),
                        PointAmount.ParseAbsolute("10")
                    )
                ),
                delivery
            ),
            "Giveaway prizes could not be awarded.",
            CommandResponseTarget.Chat
        );
        AssertSucceeded(
            _formatter.Reply(
                new PointsGiveawayDrawOutcome.Winners(
                    _settings,
                    [new PointsGiveawayWinnerPayout("viewer", PointAmount.ParseAbsolute("10"))]
                ),
                delivery
            ),
            "Giveaway winners: viewer (10).",
            CommandResponseTarget.Chat
        );
    }

    [Test]
    public void EveryCancelOutcome_Formatting_PreservesExactTextAndTarget()
    {
        var delivery = WhisperDelivery(PointsReplyKeys.GiveawayNotActive);

        AssertSucceeded(
            _formatter.Reply(new PointsGiveawayCancelOutcome.Cancelled(_settings), delivery),
            "Giveaway cancelled.",
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(new PointsGiveawayCancelOutcome.NotActive(_settings), delivery),
            "No giveaway is active.",
            CommandResponseTarget.Whisper
        );
    }

    private static ReplyDeliveryMap WhisperDelivery(params string[] replyKeys)
    {
        return ReplyDeliveryMap.FromWhisperKeys(replyKeys);
    }

    private static void AssertSucceeded(
        PointOperationOutcome outcome,
        string expectedMessage,
        CommandResponseTarget expectedTarget
    )
    {
        var succeeded = outcome.Match(
            value => value,
            _ => throw new InvalidOperationException("Expected a successful giveaway reply.")
        );
        succeeded.Message.ShouldBe(expectedMessage);
        succeeded.Target.ShouldBe(expectedTarget);
    }

    private static void AssertFailed(
        PointOperationOutcome outcome,
        string expectedMessage,
        CommandResponseTarget expectedTarget
    )
    {
        var failed = outcome.Match(
            _ => throw new InvalidOperationException("Expected a failed giveaway reply."),
            value => value
        );
        failed.Message.ShouldBe(expectedMessage);
        failed.Target.ShouldBe(expectedTarget);
    }
}
