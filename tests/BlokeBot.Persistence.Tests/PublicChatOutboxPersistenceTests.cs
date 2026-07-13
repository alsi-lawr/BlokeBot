using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class PublicChatOutboxPersistenceTests
{
    private const string _deduplicationKey =
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
                    DeduplicationKey = _deduplicationKey,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddSeconds(30),
                    NextAttemptAtUtc = now.AddSeconds(5),
                }
            );
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var row = await readDb.PublicChatOutboxMessages.AsNoTracking().SingleAsync();
        row.Channel.ShouldBe("streamer");
        row.Message.ShouldBe("durable message");
        row.DeduplicationKey.ShouldBe(_deduplicationKey);
        row.CreatedAtUtc.ShouldBe(now);
        row.ExpiresAtUtc.ShouldBe(now.AddSeconds(30));
        row.NextAttemptAtUtc.ShouldBe(now.AddSeconds(5));
        row.Status.ShouldBe(PublicChatOutboxStatus.Pending);
        row.AttemptCount.ShouldBe(0);
        row.SafePreSendFailureCount.ShouldBe(0);
        row.FailurePhase.ShouldBeNull();
        row.FailureType.ShouldBeNull();
        row.HttpStatusCode.ShouldBeNull();
        row.RejectionCode.ShouldBeNull();
        var persistedStatus = await readDb
            .Database.SqlQueryRaw<string>("SELECT Status AS Value FROM public_chat_outbox")
            .SingleAsync();
        persistedStatus.ShouldBe("Pending");
        PersistedEnumTokens<PublicChatOutboxStatus>.Values.ShouldBe([
            "Ambiguous",
            "Claimed",
            "Expired",
            "Pending",
            "Rejected",
            "SafePreSendExhausted",
            "SafePreSendTransient",
            "Sending",
            "Unexpected",
        ]);
        PersistedEnumTokens<PublicChatOutboxFailurePhase>.Values.ShouldBe(["Preparation", "Send"]);
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
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                     NextAttemptAtUtc,
                     Status, AttemptCount)
                VALUES
                    ('streamer', 'message', {_deduplicationKey}, {now}, {now.AddSeconds(30)},
                     {now}, 'Unknown', 0)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                     NextAttemptAtUtc,
                     Status, AttemptCount, SafePreSendFailureCount)
                VALUES
                    ('streamer', 'message', {_deduplicationKey}, {now}, {now.AddSeconds(30)},
                     {now}, 'Pending', 0,
                     -1)
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                     NextAttemptAtUtc,
                     Status, AttemptCount, SendStartedAtUtc, CompletedAtUtc,
                     FailurePhase, RejectionCode)
                VALUES
                    ('streamer', NULL, {_deduplicationKey}, {now}, {now.AddSeconds(30)},
                     {now}, 'Rejected', 0,
                     {now}, {now}, 'Send', 'followers_only')
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                     NextAttemptAtUtc,
                     Status, AttemptCount, CompletedAtUtc, FailurePhase, FailureType)
                VALUES
                    ('streamer', 'must be redacted', {_deduplicationKey}, {now},
                     {now.AddSeconds(30)}, {now},
                     'Unexpected', 0, {now}, 'Preparation', 'System.InvalidOperationException')
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                     NextAttemptAtUtc,
                     Status, AttemptCount, ClaimToken)
                VALUES
                    ('streamer', 'message', {_deduplicationKey}, {now}, {now.AddSeconds(30)},
                     {now}, 'Pending', 0,
                     {Guid.NewGuid()})
                """
            )
        );
        await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO public_chat_outbox
                    (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                     NextAttemptAtUtc,
                     Status, AttemptCount)
                VALUES
                    ('   ', 'message', {_deduplicationKey}, {now}, {now.AddSeconds(30)},
                     {now}, 'Pending', 0)
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
        var tableSql = await db
            .Database.SqlQueryRaw<string>(
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
        tableSql.ShouldContain("SafePreSendExhausted");
        tableSql.ShouldContain("AttemptCount > 0");
        tableSql.ShouldNotContain("'Delivered'");
        tableSql.ShouldContain("DeduplicationKey IS NULL");
        tableSql.ShouldContain("NextAttemptAtUtc IS NULL");
        tableSql.ShouldContain("'Expired'");

        var receiptTableSql = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT sql AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = 'public_chat_send_receipts'
                """
            )
            .SingleAsync();
        receiptTableSql.ShouldContain("CK_public_chat_send_receipts_Delivery");
        receiptTableSql.ShouldContain("DeliveredDeduplicationKey");

        var indexSql = await db
            .Database.SqlQueryRaw<string>(
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
            value.Contains("IX_public_chat_outbox_Status_ExpiresAtUtc", StringComparison.Ordinal)
        );
        indexSql.ShouldContain(value =>
            value.Contains(
                "UNIQUE INDEX \"IX_public_chat_outbox_ClaimToken\"",
                StringComparison.Ordinal
            ) && value.Contains("WHERE \"ClaimToken\" IS NOT NULL", StringComparison.Ordinal)
        );
        indexSql.ShouldContain(value =>
            value.Contains(
                "UNIQUE INDEX \"IX_public_chat_outbox_ClaimSlot\"",
                StringComparison.Ordinal
            ) && value.Contains("WHERE \"ClaimSlot\" IS NOT NULL", StringComparison.Ordinal)
        );
    }

    private static Task<int> InsertClaimedAsync(
        BlokeBotDbContext db,
        string message,
        Guid claimToken,
        DateTime now
    )
    {
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public_chat_outbox
                (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc,
                 NextAttemptAtUtc,
                 Status, AttemptCount, SafePreSendFailureCount, ClaimToken, ClaimSlot,
                 ClaimExpiresAtUtc)
            VALUES
                ('streamer', {message}, {_deduplicationKey}, {now}, {now.AddSeconds(30)},
                 {now}, 'Claimed', 0,
                 0, {claimToken}, 1, {now.AddMinutes(5)})
            """
        );
    }
}
