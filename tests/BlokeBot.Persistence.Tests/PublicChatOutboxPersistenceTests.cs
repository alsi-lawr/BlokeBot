using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class PublicChatOutboxPersistenceTests
{
    private const string PreviousMigration =
        "20260710183928_TypedCustomCommandVariants";
    private const string OutboxMigration = "20260712140027_PublicChatOutbox";
    private const string ClassifiedOutboxMigration =
        "20260712184117_ClassifyPublicChatDeliveryOutcomes";
    private const string RetryOutboxMigration =
        "20260712194125_RetrySafePreSendFailures";
    private const string ScheduledRetryOutboxMigration =
        "20260712212036_ScheduleMigratedSafePreSendRetries";
    private const string MarkedRetryOutboxMigration =
        "20260712212037_MarkMigratedSafePreSendRetriesForScheduling";
    private const string RetainedTerminalOutboxMigration =
        "20260712214026_RetainRedactedTerminalDeliveries";
    private const string DeduplicationKey =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Test]
    public async Task PendingOutboxMessage_RoundTripping_PreservesRequiredShapeAndToken()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        await using (var writeDb = await dbFactory.CreateDbContextAsync())
        {
            writeDb.PublicChatOutboxMessages.Add(
                new PublicChatOutboxMessage
                {
                    Channel = "streamer",
                    Message = "durable message",
                    DeduplicationKey = DeduplicationKey,
                    CreatedAtUtc = now,
                    NextAttemptAtUtc = now.AddSeconds(5),
                }
            );
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var row = await readDb
            .PublicChatOutboxMessages.AsNoTracking()
            .SingleAsync();
        row.Channel.ShouldBe("streamer");
        row.Message.ShouldBe("durable message");
        row.DeduplicationKey.ShouldBe(DeduplicationKey);
        row.CreatedAtUtc.ShouldBe(now);
        row.NextAttemptAtUtc.ShouldBe(now.AddSeconds(5));
        row.Status.ShouldBe(PublicChatOutboxStatus.Pending);
        row.AttemptCount.ShouldBe(0);
        row.SafePreSendFailureCount.ShouldBe(0);
        row.FailurePhase.ShouldBeNull();
        row.FailureType.ShouldBeNull();
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
        var persistedStatus = await readDb.Database.SqlQueryRaw<string>(
                "SELECT Status AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        persistedStatus.ShouldBe("Pending");
        PersistedEnumTokens<PublicChatOutboxStatus>.Values.ShouldBe(
            [
                "Ambiguous",
                "Claimed",
                "Pending",
                "Rejected",
                "SafePreSendExhausted",
                "SafePreSendScheduling",
                "SafePreSendTransient",
                "Sending",
                "Unexpected",
            ]
        );
        PersistedEnumTokens<PublicChatOutboxFailurePhase>.Values.ShouldBe(
            ["Preparation", "Send"]
        );
    }

    [Test]
    public async Task InvalidOutboxState_Inserting_IsRejectedByDatabaseConstraints()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount)
                VALUES
                    ('streamer', 'message', {DeduplicationKey}, {now}, {now}, 'Unknown', 0)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount, SafePreSendFailureCount, FailurePhase, FailureType)
                VALUES
                    ('streamer', 'message', {DeduplicationKey}, {now}, {now},
                     'SafePreSendScheduling', 0, 2, 'Preparation', 'System.IO.IOException')
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount, SafePreSendFailureCount)
                VALUES
                    ('streamer', 'message', {DeduplicationKey}, {now}, {now}, 'Pending', 0,
                     -1)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount, SendStartedAtUtc, CompletedAtUtc,
                     FailurePhase, RejectionCode)
                VALUES
                    ('streamer', NULL, {DeduplicationKey}, {now}, {now}, 'Rejected', 0,
                     {now}, {now}, 'Send', 'followers_only')
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount, CompletedAtUtc, FailurePhase, FailureType)
                VALUES
                    ('streamer', 'must be redacted', {DeduplicationKey}, {now}, {now},
                     'Unexpected', 0, {now}, 'Preparation', 'System.InvalidOperationException')
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount, ClaimToken)
                VALUES
                    ('streamer', 'message', {DeduplicationKey}, {now}, {now}, 'Pending', 0,
                     {Guid.NewGuid()})
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                     Status, AttemptCount)
                VALUES
                    ('   ', 'message', {DeduplicationKey}, {now}, {now}, 'Pending', 0)
                """
            )
        );
    }

    [Test]
    public async Task GlobalClaimSlot_ClaimingTwoRows_IsRejectedByUniqueFilteredIndex()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        await InsertClaimedAsync(db, "first", Guid.NewGuid(), now);

        await Should.ThrowAsync<SqliteException>(() =>
            InsertClaimedAsync(db, "second", Guid.NewGuid(), now)
        );
    }

    [Test]
    public async Task OutboxSchema_CreatingDatabase_DefinesRequiredConstraintsAndIndexes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var tableSql = await db.Database.SqlQueryRaw<string>(
                """
                SELECT sql AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = 'public_chat_outbox'
                """
            )
            .SingleAsync();
        tableSql.ShouldContain("CK_public_chat_outbox_Status");
        tableSql.ShouldContain("CK_public_chat_outbox_State");
        tableSql.ShouldContain("CK_public_chat_outbox_AttemptCount");
        tableSql.ShouldContain("CK_public_chat_outbox_SafePreSendFailureCount");
        tableSql.ShouldContain("CK_public_chat_outbox_Channel");
        tableSql.ShouldContain("CK_public_chat_outbox_DeduplicationKey");
        tableSql.ShouldContain("CK_public_chat_outbox_FailurePhase");
        tableSql.ShouldContain("SafePreSendTransient");
        tableSql.ShouldContain("SafePreSendScheduling");
        tableSql.ShouldContain("SafePreSendExhausted");
        tableSql.ShouldContain("AttemptCount > 0");
        tableSql.ShouldNotContain("'Delivered'");
        tableSql.ShouldContain("DeduplicationKey IS NULL");
        tableSql.ShouldContain("NextAttemptAtUtc IS NULL");

        var receiptTableSql = await db.Database.SqlQueryRaw<string>(
                """
                SELECT sql AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = 'public_chat_send_receipts'
                """
            )
            .SingleAsync();
        receiptTableSql.ShouldContain("CK_public_chat_send_receipts_Delivery");
        receiptTableSql.ShouldContain("DeliveredDeduplicationKey");

        var indexSql = await db.Database.SqlQueryRaw<string>(
                """
                SELECT sql AS Value
                FROM sqlite_master
                WHERE type = 'index'
                  AND tbl_name = 'public_chat_outbox'
                  AND sql IS NOT NULL
                ORDER BY name
                """
            )
            .ToArrayAsync();
        indexSql.ShouldContain(value =>
            value.Contains(
                "IX_public_chat_outbox_Status_NextAttemptAtUtc_CreatedAtUtc_Id",
                StringComparison.Ordinal
            )
        );
        indexSql.ShouldContain(value =>
            value.Contains(
                "IX_public_chat_outbox_Status_ClaimExpiresAtUtc",
                StringComparison.Ordinal
            )
        );
        indexSql.ShouldContain(value =>
            value.Contains(
                "UNIQUE INDEX \"IX_public_chat_outbox_ClaimToken\"",
                StringComparison.Ordinal
            )
            && value.Contains("WHERE \"ClaimToken\" IS NOT NULL", StringComparison.Ordinal)
        );
        indexSql.ShouldContain(value =>
            value.Contains(
                "UNIQUE INDEX \"IX_public_chat_outbox_ClaimSlot\"",
                StringComparison.Ordinal
            )
            && value.Contains("WHERE \"ClaimSlot\" IS NOT NULL", StringComparison.Ordinal)
        );
    }

    [Test]
    public async Task ClassifiedOutboxMigration_UpgradingAndReverting_MapsTransitionalFaults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new BlokeBotDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        (await TableCountAsync(db)).ShouldBe(0);

        await migrator.MigrateAsync(OutboxMigration);
        (await TableCountAsync(db)).ShouldBe(1);
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public_chat_outbox
                (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                 Status, AttemptCount, SendStartedAtUtc, CompletedAtUtc)
            VALUES
                ('streamer', NULL, {DeduplicationKey}, {now}, {now}, 'Faulted', 1,
                 {now}, {now.AddSeconds(1)})
            """
        );

        await migrator.MigrateAsync(ClassifiedOutboxMigration);
        var upgradedStatus = await db.Database.SqlQueryRaw<string>(
                "SELECT Status AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        upgradedStatus.ShouldBe("Ambiguous");
        var upgradedFailurePhase = await db.Database.SqlQueryRaw<string>(
                "SELECT FailurePhase AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        upgradedFailurePhase.ShouldBe("Send");
        var upgradedFailureType = await db.Database.SqlQueryRaw<string>(
                "SELECT FailureType AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        upgradedFailureType.ShouldBe(
            "BlokeBot.Twitch.Runtime.PublicChatUnclassifiedPostBoundaryFailure"
        );
        var upgradedMessage = await db.Database.SqlQueryRaw<string?>(
                "SELECT Message AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        upgradedMessage.ShouldBeNull();

        await migrator.MigrateAsync(OutboxMigration);
        var revertedStatus = await db.Database.SqlQueryRaw<string>(
                "SELECT Status AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        revertedStatus.ShouldBe("Faulted");

        await migrator.MigrateAsync(PreviousMigration);
        db.ChangeTracker.Clear();
        (await TableCountAsync(db)).ShouldBe(0);
    }

    [Test]
    public async Task SafePreSendRetryMigration_UpgradingAndReverting_PreservesClassifiedSafeWork()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new BlokeBotDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(ClassifiedOutboxMigration);
        var failedAt = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public_chat_outbox
                (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                 Status, AttemptCount, CompletedAtUtc, FailurePhase, FailureType)
            VALUES
                ('streamer', 'safe to retry', {DeduplicationKey}, {failedAt}, {failedAt},
                 'SafePreSendTransient', 0, {failedAt.AddSeconds(1)}, 'Preparation',
                 'System.IO.IOException')
            """
        );

        await migrator.MigrateAsync(RetryOutboxMigration);
        db.ChangeTracker.Clear();
        var upgraded = await db.PublicChatOutboxMessages.SingleAsync();
        upgraded.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendTransient);
        upgraded.Message.ShouldBe("safe to retry");
        upgraded.AttemptCount.ShouldBe(0);
        upgraded.SafePreSendFailureCount.ShouldBe(1);
        upgraded.NextAttemptAtUtc.ShouldBe(failedAt.AddSeconds(1));
        upgraded.CompletedAtUtc.ShouldBeNull();

        await migrator.MigrateAsync(ScheduledRetryOutboxMigration);
        db.ChangeTracker.Clear();
        var schemaUpgraded = await db.PublicChatOutboxMessages.SingleAsync();
        schemaUpgraded.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendTransient);

        await migrator.MigrateAsync(MarkedRetryOutboxMigration);
        db.ChangeTracker.Clear();
        var pendingSchedule = await db.PublicChatOutboxMessages.SingleAsync();
        pendingSchedule.Status.ShouldBe(PublicChatOutboxStatus.SafePreSendScheduling);
        pendingSchedule.Message.ShouldBe("safe to retry");
        pendingSchedule.SafePreSendFailureCount.ShouldBe(1);
        pendingSchedule.NextAttemptAtUtc.ShouldBe(failedAt.AddSeconds(1));
        pendingSchedule.CompletedAtUtc.ShouldBeNull();

        await migrator.MigrateAsync(ScheduledRetryOutboxMigration);
        var revertedSchedulingStatus = await db.Database.SqlQueryRaw<string>(
                "SELECT Status AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        revertedSchedulingStatus.ShouldBe("SafePreSendTransient");

        await migrator.MigrateAsync(RetryOutboxMigration);
        await migrator.MigrateAsync(ClassifiedOutboxMigration);
        var revertedStatus = await db.Database.SqlQueryRaw<string>(
                "SELECT Status AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        revertedStatus.ShouldBe("SafePreSendTransient");
    }

    [Test]
    public async Task TerminalRetentionMigration_UpgradingAndReverting_DeletesDeliveredAndRedactsTerminalRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new BlokeBotDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(MarkedRetryOutboxMigration);
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public_chat_outbox
                (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                 Status, AttemptCount, SafePreSendFailureCount, SendStartedAtUtc,
                 CompletedAtUtc, FailurePhase, RejectionCode)
            VALUES
                ('streamer', NULL, {DeduplicationKey}, {now}, {now}, 'Delivered', 1, 0,
                 {now}, {now.AddSeconds(1)}, NULL, NULL),
                ('streamer', NULL, {DeduplicationKey}, {now}, {now}, 'Rejected', 1, 0,
                 {now.AddSeconds(2)}, {now.AddSeconds(3)}, 'Send', 'followers_only')
            """
        );

        await migrator.MigrateAsync(RetainedTerminalOutboxMigration);
        db.ChangeTracker.Clear();
        var retained = await db.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        retained.Status.ShouldBe(PublicChatOutboxStatus.Rejected);
        retained.Message.ShouldBeNull();
        retained.DeduplicationKey.ShouldBeNull();
        retained.NextAttemptAtUtc.ShouldBeNull();
        var sendReceipts = await db
            .PublicChatSendReceipts.AsNoTracking()
            .OrderBy(receipt => receipt.OutboxMessageId)
            .ToArrayAsync();
        sendReceipts.Length.ShouldBe(2);
        sendReceipts[0].DeliveredDeduplicationKey.ShouldBe(DeduplicationKey);
        sendReceipts[0].DeliveredAtUtc.ShouldBe(now.AddSeconds(1));
        sendReceipts[1].DeliveredDeduplicationKey.ShouldBeNull();
        sendReceipts[1].CompletedAtUtc.ShouldBe(now.AddSeconds(3));

        await migrator.MigrateAsync(MarkedRetryOutboxMigration);
        var downgraded = await db.Database.SqlQueryRaw<DowngradedTerminalRow>(
                """
                SELECT DeduplicationKey, NextAttemptAtUtc
                FROM public_chat_outbox
                """
            )
            .SingleAsync();
        downgraded.DeduplicationKey.ShouldBe(
            "0000000000000000000000000000000000000000000000000000000000000000"
        );
        downgraded.NextAttemptAtUtc.ShouldBe(now.AddSeconds(3));
        var receiptTableCount = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = 'public_chat_send_receipts'
                """
            )
            .SingleAsync();
        receiptTableCount.ShouldBe(0);
    }

    private sealed record DowngradedTerminalRow(
        string DeduplicationKey,
        DateTime NextAttemptAtUtc
    );

    private static Task<int> InsertClaimedAsync(
        BlokeBotDbContext db,
        string message,
        Guid claimToken,
        DateTime now
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public_chat_outbox
                (Channel, Message, DeduplicationKey, CreatedAtUtc, NextAttemptAtUtc,
                 Status, AttemptCount, SafePreSendFailureCount, ClaimToken, ClaimSlot,
                 ClaimExpiresAtUtc)
            VALUES
                ('streamer', {message}, {DeduplicationKey}, {now}, {now}, 'Claimed', 0,
                 0, {claimToken}, 1, {now.AddMinutes(5)})
            """
        );

    private static Task<int> TableCountAsync(BlokeBotDbContext db) =>
        db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = 'public_chat_outbox'
                """
            )
            .SingleAsync();
}
