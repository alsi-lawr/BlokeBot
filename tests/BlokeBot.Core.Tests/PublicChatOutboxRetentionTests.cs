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

public sealed class PublicChatOutboxRetentionTests : PublicChatOutboxIntegrationTestBase
{
    [Test]
    public async Task TerminalRetention_CleanupAtExactCutoff_PreservesOnlyNewerRowUntilDue()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var duration = TimeSpan.FromMinutes(10);
        await SeedTerminalRowsAsync(
            dbFactory,
            TerminalRow(PublicChatOutboxStatus.Unexpected, now - duration - TimeSpan.FromTicks(1)),
            TerminalRow(PublicChatOutboxStatus.Rejected, now - duration),
            TerminalRow(PublicChatOutboxStatus.Ambiguous, now - duration + TimeSpan.FromTicks(1))
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(duration)
        );

        var beforeFinalCutoff = await outbox.TryClaimNextAsync(
            now,
            now.AddMinutes(5),
            TimeSpan.Zero,
            TimeSpan.Zero,
            CancellationToken.None
        );

        beforeFinalCutoff
            .ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>()
            .AvailableAt.ShouldBe(now.AddTicks(1));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var retained = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            retained.Status.ShouldBe(PublicChatOutboxStatus.Ambiguous);
        }

        (
            await new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                Retention(duration)
            ).TryClaimNextAsync(
                now.AddTicks(1),
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var verification = await dbFactory.CreateDbContextAsync();
        (await verification.PublicChatOutboxMessages.AsNoTracking().ToArrayAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task ExpiredTerminal_RetentionAtExactCutoff_PurgesWithOtherTerminalRows()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var duration = TimeSpan.FromMinutes(10);
        await SeedTerminalRowsAsync(
            dbFactory,
            TerminalRow(PublicChatOutboxStatus.Expired, now - duration)
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(duration)
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task MissingIdentityTerminals_RetentionAtExactCutoff_PurgesBothCases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        var duration = TimeSpan.FromMinutes(10);
        await SeedTerminalRowsAsync(
            dbFactory,
            TerminalRow(PublicChatOutboxStatus.MissingChannel, now - duration),
            TerminalRow(PublicChatOutboxStatus.MissingBot, now - duration)
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(duration)
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task TerminalRetention_MoreThanOneBatch_CleansInBoundedPasses()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        await SeedTerminalRowsAsync(
            dbFactory,
            [
                .. Enumerable
                    .Range(0, 101)
                    .Select(index =>
                        TerminalRow(
                            PublicChatOutboxStatus.Unexpected,
                            now.AddMinutes(-20).AddTicks(index)
                        )
                    ),
            ]
        );
        var outbox = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(TimeSpan.FromMinutes(10))
        );

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.AwaitingAvailability>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.PublicChatOutboxMessages.CountAsync()).ShouldBe(1);
        }

        (
            await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            )
        ).ShouldBeOfType<PublicChatClaimOutcome.Empty>();
        await using var verification = await dbFactory.CreateDbContextAsync();
        (await verification.PublicChatOutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task TerminalRetention_CleanupNeverDeletesPendingRetryOrInFlightStates()
    {
        PublicChatOutboxStatus[] outstandingStatuses =
        [
            PublicChatOutboxStatus.Pending,
            PublicChatOutboxStatus.Claimed,
            PublicChatOutboxStatus.Sending,
            PublicChatOutboxStatus.SafePreSendTransient,
        ];
        var now = Utc(12, 0, 0);
        foreach (var status in outstandingStatuses)
        {
            await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var outstanding = OutstandingRow(status, now);
                db.PublicChatOutboxMessages.Add(outstanding);
                db.PublicChatOutboxMessages.Add(
                    TerminalRow(PublicChatOutboxStatus.Unexpected, now.AddMinutes(-20))
                );
                await db.SaveChangesAsync();
                if (status == PublicChatOutboxStatus.Sending)
                {
                    db.PublicChatSendReceipts.Add(
                        new PublicChatSendReceipt
                        {
                            OutboxMessageId = outstanding.Id,
                            AttemptedAtUtc = outstanding.SendStartedAtUtc!.Value,
                        }
                    );
                    await db.SaveChangesAsync();
                }
            }
            var outbox = new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                Retention(TimeSpan.FromMinutes(10))
            );

            _ = await outbox.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            );

            await using var verification = await dbFactory.CreateDbContextAsync();
            var retained = await verification.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
            retained.Message.ShouldBe("must survive terminal cleanup");
            retained.Status.ShouldNotBe(PublicChatOutboxStatus.Unexpected);
        }
    }

    [Test]
    public async Task TerminalRetention_ConcurrentCleanupAndClaim_UsesDistinctConnectionsSafely()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = Utc(12, 0, 0);
        await SeedTerminalRowsAsync(
            dbFactory,
            [
                .. Enumerable
                    .Range(0, 101)
                    .Select(index =>
                        TerminalRow(
                            PublicChatOutboxStatus.Unexpected,
                            now.AddMinutes(-20).AddTicks(index)
                        )
                    ),
            ]
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var pending = OutstandingRow(PublicChatOutboxStatus.Pending, now);
            pending.NextAttemptAtUtc = now.UtcDateTime;
            db.PublicChatOutboxMessages.Add(pending);
            await db.SaveChangesAsync();
        }
        await using var firstContext = await dbFactory.CreateDbContextAsync();
        await using var secondContext = await dbFactory.CreateDbContextAsync();
        firstContext.ShouldNotBeSameAs(secondContext);
        firstContext
            .Database.GetDbConnection()
            .ShouldNotBeSameAs(secondContext.Database.GetDbConnection());
        var firstStore = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(TimeSpan.FromMinutes(10))
        );
        var secondStore = new EfPublicChatOutbox(
            dbFactory,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            Retention(TimeSpan.FromMinutes(10))
        );

        var outcomes = await Task.WhenAll(
            firstStore
                .TryClaimNextAsync(
                    now,
                    now.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask(),
            secondStore
                .TryClaimNextAsync(
                    now,
                    now.AddMinutes(5),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    CancellationToken.None
                )
                .AsTask()
        );
        if (outcomes.OfType<PublicChatClaimOutcome.Claimed>().Count() == 0)
        {
            _ = await firstStore.TryClaimNextAsync(
                now,
                now.AddMinutes(5),
                TimeSpan.Zero,
                TimeSpan.Zero,
                CancellationToken.None
            );
        }

        await using var verification = await dbFactory.CreateDbContextAsync();
        var liveRows = await verification
            .PublicChatOutboxMessages.AsNoTracking()
            .Where(row => row.Status == PublicChatOutboxStatus.Claimed)
            .ToArrayAsync();
        liveRows.ShouldHaveSingleItem().Message.ShouldBe("must survive terminal cleanup");
        (
            await verification
                .PublicChatOutboxMessages.AsNoTracking()
                .CountAsync(row => row.Status == PublicChatOutboxStatus.Unexpected)
        ).ShouldBeLessThanOrEqualTo(1);
    }

    [Test]
    public async Task DatabaseUnavailable_Enqueueing_ReportsFailureWithoutDelivery()
    {
        var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await dbFactory.DisposeAsync();
        var transport = new RecordingPublicChatTransport();
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

        var outcome = await queue.EnqueueAsync(
            Command("streamer", "not accepted"),
            CancellationToken.None
        );
        outcome
            .ShouldBeOfType<PublicChatEnqueueOutcome.Ambiguous>()
            .Cause.ShouldBeOfType<DbUpdateException>();
        transport.DeliveryCount.ShouldBe(0);
    }
}
