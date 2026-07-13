using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed record WhisperQuotaStatus(int RecipientCount, int Limit, bool Exhausted)
{
    public int Remaining => Math.Max(0, Limit - RecipientCount);
}

public abstract record WhisperQuotaReservation
{
    private WhisperQuotaReservation() { }

    public abstract WhisperQuotaStatus Status { get; init; }

    public sealed record ExistingRecipient(WhisperQuotaStatus Status) : WhisperQuotaReservation;

    public sealed record NewRecipient(WhisperQuotaStatus Status) : WhisperQuotaReservation;
}

public abstract record WhisperQuotaReservationError
{
    private WhisperQuotaReservationError() { }

    public sealed record InvalidIdentity : WhisperQuotaReservationError;

    public sealed record DailyRecipientLimitReached(WhisperQuotaStatus Status)
        : WhisperQuotaReservationError;
}

public sealed class HostWhisperQuotaService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    public const int UniqueRecipientLimit = 40;

    public async Task<WhisperQuotaStatus> GetStatusAsync(
        int hostId,
        string? botTwitchUserId,
        CancellationToken ct
    )
    {
        var botUserId = NormalizeId(botTwitchUserId);
        if (string.IsNullOrWhiteSpace(botUserId))
        {
            return EmptyStatus();
        }

        var day = CurrentDayUtc();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bucket = await db
            .WhisperQuotaBuckets.AsNoTracking()
            .Where(x => x.HostId == hostId && x.BotTwitchUserId == botUserId && x.DayUtc == day)
            .Select(x => new { x.Exhausted, RecipientCount = x.Recipients.Count })
            .SingleOrDefaultAsync(ct);

        return bucket is null
            ? EmptyStatus()
            : new WhisperQuotaStatus(bucket.RecipientCount, UniqueRecipientLimit, bucket.Exhausted);
    }

    public IO<WhisperQuotaReservation, WhisperQuotaReservationError> ReserveRecipient(
        int hostId,
        string botTwitchUserId,
        string recipientTwitchUserId,
        string recipientLogin
    )
    {
        return IO<WhisperQuotaReservation, WhisperQuotaReservationError>.Create(ct =>
            PersistReservationAsync(
                hostId,
                botTwitchUserId,
                recipientTwitchUserId,
                recipientLogin,
                ct
            )
        );
    }

    private async ValueTask<
        Result<WhisperQuotaReservation, WhisperQuotaReservationError>
    > PersistReservationAsync(
        int hostId,
        string botTwitchUserId,
        string recipientTwitchUserId,
        string recipientLogin,
        CancellationToken ct
    )
    {
        var botUserId = NormalizeId(botTwitchUserId);
        var recipientUserId = NormalizeId(recipientTwitchUserId);
        if (string.IsNullOrWhiteSpace(botUserId) || string.IsNullOrWhiteSpace(recipientUserId))
        {
            return Result<WhisperQuotaReservation, WhisperQuotaReservationError>.Error(
                new WhisperQuotaReservationError.InvalidIdentity()
            );
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var day = now.Date;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bucket = await db
            .WhisperQuotaBuckets.Include(x => x.Recipients)
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.BotTwitchUserId == botUserId && x.DayUtc == day,
                ct
            );
        if (bucket is null)
        {
            bucket = new WhisperQuotaBucket
            {
                HostId = hostId,
                BotTwitchUserId = botUserId,
                DayUtc = day,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.WhisperQuotaBuckets.Add(bucket);
        }

        var existing = bucket.Recipients.Any(x =>
            x.RecipientTwitchUserId.Equals(recipientUserId, StringComparison.Ordinal)
        );
        if (existing)
        {
            return Success(new WhisperQuotaReservation.ExistingRecipient(Status(bucket)));
        }

        if (bucket.Exhausted || bucket.Recipients.Count >= UniqueRecipientLimit)
        {
            bucket.Exhausted = true;
            bucket.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return Result<WhisperQuotaReservation, WhisperQuotaReservationError>.Error(
                new WhisperQuotaReservationError.DailyRecipientLimitReached(Status(bucket))
            );
        }

        bucket.Recipients.Add(
            new WhisperQuotaRecipient
            {
                RecipientTwitchUserId = recipientUserId,
                RecipientLogin = TwitchLogin.Normalize(recipientLogin),
                FirstSentAtUtc = now,
            }
        );
        bucket.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Success(new WhisperQuotaReservation.NewRecipient(Status(bucket)));
    }

    public async Task MarkExhaustedAsync(int hostId, string botTwitchUserId, CancellationToken ct)
    {
        var botUserId = NormalizeId(botTwitchUserId);
        if (string.IsNullOrWhiteSpace(botUserId))
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var day = now.Date;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bucket = await db.WhisperQuotaBuckets.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.BotTwitchUserId == botUserId && x.DayUtc == day,
            ct
        );
        if (bucket is null)
        {
            bucket = new WhisperQuotaBucket
            {
                HostId = hostId,
                BotTwitchUserId = botUserId,
                DayUtc = day,
                CreatedAtUtc = now,
            };
            db.WhisperQuotaBuckets.Add(bucket);
        }

        bucket.Exhausted = true;
        bucket.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    private DateTime CurrentDayUtc()
    {
        return clock.GetUtcNow().UtcDateTime.Date;
    }

    private static string NormalizeId(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static WhisperQuotaStatus EmptyStatus()
    {
        return new(0, UniqueRecipientLimit, false);
    }

    private static Result<WhisperQuotaReservation, WhisperQuotaReservationError> Success(
        WhisperQuotaReservation reservation
    )
    {
        return Result<WhisperQuotaReservation, WhisperQuotaReservationError>.Success(reservation);
    }

    private static WhisperQuotaStatus Status(WhisperQuotaBucket bucket)
    {
        return new(bucket.Recipients.Count, UniqueRecipientLimit, bucket.Exhausted);
    }
}
