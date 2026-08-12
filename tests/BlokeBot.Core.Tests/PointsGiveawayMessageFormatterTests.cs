using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence.Models;
using Shouldly;

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
            null,
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
            null,
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
            null,
            CommandResponseTarget.Whisper
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayStartOutcome.StreamLivenessUnavailable(_settings, unavailable),
                delivery
            ),
            null,
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(
                new PointsGiveawayStartOutcome.FollowerEligibilityUnavailable(_settings),
                delivery
            ),
            null,
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
            null,
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
            null,
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
            null,
            CommandResponseTarget.Whisper
        );
        AssertSucceeded(
            _formatter.Reply(new PointsGiveawayDrawOutcome.NoEntrants(_settings), delivery),
            null,
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
            null,
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
            null,
            CommandResponseTarget.Chat
        );
        AssertFailed(
            _formatter.Reply(new PointsGiveawayCancelOutcome.NotActive(_settings), delivery),
            null,
            CommandResponseTarget.Whisper
        );
    }

    private static ReplyDeliveryMap WhisperDelivery(params string[] replyKeys) =>
        ReplyDeliveryMap.FromWhisperKeys(replyKeys);

    private static void AssertSucceeded(
        PointOperationOutcome outcome,
        string? expectedMessage,
        CommandResponseTarget expectedTarget
    )
    {
        var succeeded = outcome.Match(
            static value => value,
            static _ => throw new InvalidOperationException("Expected a successful giveaway reply.")
        );
        if (expectedMessage is not null)
        {
            succeeded.Message.ShouldBe(expectedMessage);
        }
        succeeded.Target.ShouldBe(expectedTarget);
    }

    private static void AssertFailed(
        PointOperationOutcome outcome,
        string? expectedMessage,
        CommandResponseTarget expectedTarget
    )
    {
        var failed = outcome.Match(
            static _ => throw new InvalidOperationException("Expected a failed giveaway reply."),
            static value => value
        );
        if (expectedMessage is not null)
        {
            failed.Message.ShouldBe(expectedMessage);
        }
        failed.Target.ShouldBe(expectedTarget);
    }
}
