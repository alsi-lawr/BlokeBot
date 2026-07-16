using System.Collections.Immutable;
using System.Diagnostics;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Shouldly;
using TUnit.Core;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class PublicChatOutboxInterruptionTests : PublicChatOutboxIntegrationTestBase
{
    [Test]
    public async Task CallerCanceledDuringPreparation_PersistingState_ReleasesPendingWithoutAttempt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var transport = new ScriptedPublicChatTransport(
            (_, cancellationToken) =>
            {
                stopping.Cancel();
                return ValueTask.FromException<PublicChatPreparationOutcome>(
                    new OperationCanceledException(cancellationToken)
                );
            },
            static (_, _) =>
                throw new InvalidOperationException("Canceled preparation cannot send.")
        );
        var queue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            transport,
            new ManualTestTimeProvider(Utc(12, 0, 0))
        );
        _ = await queue.EnqueueAsync(Command("streamer", "still pending"), CancellationToken.None);

        await queue.RunAsync(stopping.Token);

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Status.ShouldBe(PublicChatOutboxStatus.Pending);
        row.Message.ShouldBe("still pending");
        row.AttemptCount.ShouldBe(0);
        row.SendStartedAtUtc.ShouldBeNull();
        row.CompletedAtUtc.ShouldBeNull();
        row.ClaimToken.ShouldBeNull();
        transport.SendCount.ShouldBe(0);
    }

    [Test]
    public async Task CallerCanceledAfterSendBoundary_PersistingState_IsAmbiguousAndNeverReclaimed()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var transport = new ScriptedPublicChatTransport(
            Ready,
            (_, cancellationToken) =>
            {
                stopping.Cancel();
                return ValueTask.FromException<PublicChatTransportSendResult>(
                    new OperationCanceledException(cancellationToken)
                );
            }
        );
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var queue = CreateQueue(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            ),
            transport,
            clock
        );
        _ = await queue.EnqueueAsync(
            Command("streamer", "redact after boundary"),
            CancellationToken.None
        );

        await queue.RunAsync(stopping.Token);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.Ambiguous);
            row.Message.ShouldBeNull();
            row.AttemptCount.ShouldBe(1);
            row.SendStartedAtUtc.ShouldNotBeNull();
            row.FailurePhase.ShouldBe(PublicChatOutboxFailurePhase.Send);
            row.FailureType.ShouldBe(typeof(OperationCanceledException).FullName);
            row.RejectionCode.ShouldBeNull();
        }

        var afterRestart = await new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        ).TryClaimNextAsync(
            clock.GetUtcNow(),
            clock.GetUtcNow().AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );
        afterRestart.ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
    }

    [Test]
    public async Task ConfirmedSend_ReceiptPersistenceFails_RollsBackClaimedRowDeletion()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy
        );
        _ = await outbox.EnqueueAsync(
            Batch("streamer", now, "atomic success"),
            CancellationToken.None
        );
        var claimed = await ClaimAsync(outbox, now, TimeSpan.Zero);
        (
            await outbox.BeginSendAsync(claimed, now, now.AddMinutes(5), CancellationToken.None)
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed with
                {
                    ClaimToken = new PublicChatClaimToken(Guid.NewGuid()),
                },
                new PublicChatDeliveryOutcome.Sent(),
                now.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.OwnershipLost>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_public_chat_receipt_update
                BEFORE UPDATE ON public_chat_send_receipts
                BEGIN
                    SELECT RAISE(ABORT, 'receipt update failed');
                END;
                """
            );
        }

        await Should.ThrowAsync<SqliteException>(() =>
            outbox
                .RecordDeliveryOutcomeAsync(
                    claimed,
                    new PublicChatDeliveryOutcome.Sent(),
                    now.AddSeconds(1),
                    CancellationToken.None
                )
                .AsTask()
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var row = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            row.Status.ShouldBe(PublicChatOutboxStatus.Sending);
            row.Message.ShouldBe("atomic success");
            var sendReceipt = await db.PublicChatSendReceipts.AsNoTracking().SingleAsync();
            sendReceipt.DeliveredDeduplicationKey.ShouldBeNull();
            sendReceipt.DeliveredAtUtc.ShouldBeNull();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_public_chat_receipt_update;");
        }

        (
            await outbox.RecordDeliveryOutcomeAsync(
                claimed,
                new PublicChatDeliveryOutcome.Sent(),
                now.AddSeconds(1),
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
        await using var verification = await dbFactory.CreateDbContextAsync();
        (await verification.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
    }
}
