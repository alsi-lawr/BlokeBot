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
        var persistedStatus = await readDb.Database.SqlQueryRaw<string>(
                "SELECT Status AS Value FROM public_chat_outbox"
            )
            .SingleAsync();
        persistedStatus.ShouldBe("Pending");
        PersistedEnumTokens<PublicChatOutboxStatus>.Values.ShouldBe(
            ["Claimed", "Delivered", "Faulted", "Pending", "Sending"]
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
        tableSql.ShouldContain("CK_public_chat_outbox_Channel");
        tableSql.ShouldContain("CK_public_chat_outbox_DeduplicationKey");

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
    public async Task OutboxMigration_ApplyingAndReverting_CreatesRoundTrippableSchema()
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
        db.PublicChatOutboxMessages.Add(
            new PublicChatOutboxMessage
            {
                Channel = "streamer",
                Message = "migrated",
                DeduplicationKey = DeduplicationKey,
                CreatedAtUtc = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc),
                NextAttemptAtUtc = new DateTime(
                    2026,
                    7,
                    12,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc
                ),
            }
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.PublicChatOutboxMessages.SingleAsync()).Message.ShouldBe("migrated");

        await migrator.MigrateAsync(PreviousMigration);
        db.ChangeTracker.Clear();
        (await TableCountAsync(db)).ShouldBe(0);
    }

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
                 Status, AttemptCount, ClaimToken, ClaimSlot, ClaimExpiresAtUtc)
            VALUES
                ('streamer', {message}, {DeduplicationKey}, {now}, {now}, 'Claimed', 0,
                 {claimToken}, 1, {now.AddMinutes(5)})
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
