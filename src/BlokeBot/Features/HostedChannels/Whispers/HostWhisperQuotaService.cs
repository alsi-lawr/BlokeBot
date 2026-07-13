using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed record WhisperQuotaStatus(int RecipientCount, int Limit, bool Exhausted)
{
    public int Remaining => Math.Max(0, Limit - RecipientCount);
}

public enum WhisperQuotaReservationBlockReason
{
    DailyRecipientLimitReached,
}

public sealed record WhisperQuotaReservationResult(
    bool Allowed,
    bool CountedNewRecipient,
    WhisperQuotaReservationBlockReason? BlockReason,
    WhisperQuotaStatus Status
);

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

    public async Task<WhisperQuotaReservationResult> ReserveRecipientAsync(
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
            return Blocked(EmptyStatus());
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
            return Allowed(bucket, countedNewRecipient: false);
        }

        if (bucket.Exhausted || bucket.Recipients.Count >= UniqueRecipientLimit)
        {
            bucket.Exhausted = true;
            bucket.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return Blocked(bucket);
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
        return Allowed(bucket, countedNewRecipient: true);
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

    private static WhisperQuotaReservationResult Allowed(
        WhisperQuotaBucket bucket,
        bool countedNewRecipient
    )
    {
        return new(
            true,
            countedNewRecipient,
            null,
            new WhisperQuotaStatus(bucket.Recipients.Count, UniqueRecipientLimit, bucket.Exhausted)
        );
    }

    private static WhisperQuotaReservationResult Blocked(WhisperQuotaBucket bucket)
    {
        return Blocked(new WhisperQuotaStatus(bucket.Recipients.Count, UniqueRecipientLimit, true));
    }

    private static WhisperQuotaReservationResult Blocked(WhisperQuotaStatus status)
    {
        return new(false, false, WhisperQuotaReservationBlockReason.DailyRecipientLimitReached, status);
    }
}
