using System.Collections.Immutable;
using System.Diagnostics;
using BlokeBot.Persistence.Models;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public abstract class PublicChatOutboxIntegrationTestBase
{
    private protected static PublicChatOutboxBatch Batch(
        string channel,
        DateTimeOffset enqueuedAt,
        params string[] messages
    ) =>
        new()
        {
            Channel = channel,
            EnqueuedAt = enqueuedAt,
            Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            Items = messages
                .Select(message => new PublicChatOutboxItem
                {
                    Message = message,
                    DeduplicationKey = PublicChatMessageDeduplication.Key(channel, message),
                })
                .ToImmutableArray(),
        };

    private protected static async Task<PublicChatClaimedMessage> ClaimAsync(
        IPublicChatOutbox outbox,
        DateTimeOffset now,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown = default
    ) =>
        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                sendInterval,
                duplicateCooldown,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<PublicChatClaimOutcome.Claimed>()
            .Message;

    private protected static async Task BeginAndDeliverAsync(
        IPublicChatOutbox outbox,
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset deliveredAt
    )
    {
        (
            await outbox.BeginSendAsync(
                message,
                sendStartedAt,
                sendStartedAt.AddMinutes(5),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (
            await outbox.RecordDeliveryOutcomeAsync(
                message,
                new PublicChatDeliveryOutcome.Sent(),
                deliveredAt,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
    }

    private protected static ValueTask<PublicChatPreparationOutcome> Ready(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<PublicChatPreparationOutcome>(
            new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
        );
    }

    private protected static PublicChatDeliveryOutcome SafePreSendTransientOutcome() =>
        PublicChatDeliveryClassifier.MapPreparationFailure(
            PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                new IOException("secret preparation detail"),
                CancellationToken.None
            )
        );

    private protected static PublicChatTerminalRetentionPolicy Retention(TimeSpan duration) =>
        new() { Duration = duration };

    private protected static PublicChatDeliveryLifetimePolicy Lifetime(TimeSpan maximumAge) =>
        new() { MaximumAge = maximumAge };

    private protected static void AssertExpired(
        PublicChatOutboxMessage row,
        DateTimeOffset completedAt
    )
    {
        row.Status.ShouldBe(PublicChatOutboxStatus.Expired);
        row.Message.ShouldBeNull();
        row.DeduplicationKey.ShouldBeNull();
        row.NextAttemptAtUtc.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        row.ClaimSlot.ShouldBeNull();
        row.ClaimExpiresAtUtc.ShouldBeNull();
        row.SendStartedAtUtc.ShouldBeNull();
        row.CompletedAtUtc.ShouldBe(completedAt.UtcDateTime);
        row.FailurePhase.ShouldBeNull();
        row.FailureType.ShouldBeNull();
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
    }

    private protected static async Task SeedTerminalRowsAsync(
        SqliteBlokeBotDbFactory dbFactory,
        params PublicChatOutboxMessage[] rows
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.PublicChatOutboxMessages.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private protected static PublicChatOutboxMessage TerminalRow(
        PublicChatOutboxStatus status,
        DateTimeOffset completedAt
    )
    {
        var row = new PublicChatOutboxMessage
        {
            Channel = "streamer",
            CreatedAtUtc = completedAt.AddHours(-1).UtcDateTime,
            ExpiresAtUtc = completedAt.AddMinutes(-59).UtcDateTime,
            CompletedAtUtc = completedAt.UtcDateTime,
            Status = status,
        };
        switch (status)
        {
            case PublicChatOutboxStatus.SafePreSendExhausted:
                row.SafePreSendFailureCount = 1;
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                row.FailureType = typeof(IOException).FullName;
                break;
            case PublicChatOutboxStatus.MissingChannel:
            case PublicChatOutboxStatus.MissingBot:
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                break;
            case PublicChatOutboxStatus.Rejected:
                row.AttemptCount = 1;
                row.SendStartedAtUtc = completedAt.AddSeconds(-1).UtcDateTime;
                row.FailurePhase = PublicChatOutboxFailurePhase.Send;
                row.RejectionCode = "followers_only";
                break;
            case PublicChatOutboxStatus.Ambiguous:
                row.AttemptCount = 1;
                row.SendStartedAtUtc = completedAt.AddSeconds(-1).UtcDateTime;
                row.FailurePhase = PublicChatOutboxFailurePhase.Send;
                row.FailureType = typeof(IOException).FullName;
                break;
            case PublicChatOutboxStatus.Unexpected:
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                row.FailureType = typeof(InvalidOperationException).FullName;
                break;
            case PublicChatOutboxStatus.Expired:
                break;
            default:
                throw new UnreachableException($"{status} is not a terminal public chat status.");
        }

        return row;
    }

    private protected static PublicChatOutboxMessage OutstandingRow(
        PublicChatOutboxStatus status,
        DateTimeOffset now
    )
    {
        var row = new PublicChatOutboxMessage
        {
            Channel = "streamer",
            Message = "must survive terminal cleanup",
            DeduplicationKey = PublicChatMessageDeduplication
                .Key("streamer", "must survive terminal cleanup")
                .Value,
            CreatedAtUtc = now.AddHours(-1).UtcDateTime,
            ExpiresAtUtc = now.AddHours(2).UtcDateTime,
            NextAttemptAtUtc = now.AddHours(1).UtcDateTime,
            Status = status,
        };
        switch (status)
        {
            case PublicChatOutboxStatus.Pending:
                break;
            case PublicChatOutboxStatus.Claimed:
                row.ClaimToken = Guid.NewGuid();
                row.ClaimSlot = 1;
                row.ClaimExpiresAtUtc = now.AddHours(1).UtcDateTime;
                break;
            case PublicChatOutboxStatus.Sending:
                row.AttemptCount = 1;
                row.ClaimToken = Guid.NewGuid();
                row.ClaimSlot = 1;
                row.ClaimExpiresAtUtc = now.AddHours(1).UtcDateTime;
                row.SendStartedAtUtc = now.UtcDateTime;
                break;
            case PublicChatOutboxStatus.SafePreSendTransient:
                row.SafePreSendFailureCount = 1;
                row.FailurePhase = PublicChatOutboxFailurePhase.Preparation;
                row.FailureType = typeof(IOException).FullName;
                break;
            default:
                throw new UnreachableException($"{status} is not outstanding public chat work.");
        }

        return row;
    }

    private protected sealed record TerminalScenario
    {
        internal required PublicChatOutboxStatus ExpectedStatus { get; init; }

        internal required PublicChatOutboxFailurePhase ExpectedPhase { get; init; }

        internal required string? ExpectedFailureType { get; init; }

        internal required string? ExpectedRejectionCode { get; init; }

        internal required int ExpectedInitialSendCount { get; init; }

        internal required Func<ScriptedPublicChatTransport> CreateTransport { get; init; }
    }
}
