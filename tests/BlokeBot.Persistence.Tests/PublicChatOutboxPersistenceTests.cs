using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

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
            "MissingBot",
            "MissingChannel",
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

    private static Task<int> InsertClaimedAsync(
        BlokeBotDbContext db,
        string message,
        Guid claimToken,
        DateTime now
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
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
