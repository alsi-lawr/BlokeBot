using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class PublicChatOutboxPersistenceTests
{
    private const string _deduplicationKey =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Test]
    public async Task InvalidOutboxState_Inserting_IsRejectedByDatabaseConstraints()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

        _ = await Should.ThrowAsync<SqliteException>(() =>
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
        _ = await Should.ThrowAsync<SqliteException>(() =>
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
        _ = await Should.ThrowAsync<SqliteException>(() =>
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
        _ = await Should.ThrowAsync<SqliteException>(() =>
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
        _ = await Should.ThrowAsync<SqliteException>(() =>
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
        _ = await Should.ThrowAsync<SqliteException>(() =>
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
        _ = await InsertClaimedAsync(db, "first", Guid.NewGuid(), now);

        _ = await Should.ThrowAsync<SqliteException>(() =>
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
