using BlokeBot.Core.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.Points.Giveaways;

internal interface IPointsGiveawaySchedulerOperations
{
    IO<IReadOnlyList<PointsGiveawaySchedule>, PointsGiveawaySchedulerTransientFailure> LoadActive();

    IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildUpdate(
        int giveawayId,
        DateTime endsAtUtc
    );

    IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure> Draw(int giveawayId);

    IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildDrawNotification(
        PointsGiveawayDrawOutcome outcome
    );

    IO<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerTransientFailure> Expire(
        int giveawayId
    );

    IO<
        PointsGiveawayChangeNotificationCompleted,
        PointsGiveawaySchedulerNotificationFailure
    > NotifyChanged(int hostId);
}

internal sealed class PointsGiveawaySchedulerOperations(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointsGiveawayDrawService draws,
    PointsGiveawayMessageFormatter formatter,
    IPointsGiveawayChangeNotification changeNotification,
    TimeProvider timeProvider
) : IPointsGiveawaySchedulerOperations
{
    public IO<
        IReadOnlyList<PointsGiveawaySchedule>,
        PointsGiveawaySchedulerTransientFailure
    > LoadActive()
    {
        return CaptureDurable(LoadActiveAsync);
    }

    public IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildUpdate(
        int giveawayId,
        DateTime endsAtUtc
    )
    {
        return CaptureNotification(ct => BuildUpdateAsync(giveawayId, endsAtUtc, ct));
    }

    public IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure> Draw(
        int giveawayId
    )
    {
        return CaptureDurable(ct => DrawAsync(giveawayId, ct));
    }

    public IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildDrawNotification(
        PointsGiveawayDrawOutcome outcome
    )
    {
        return CaptureNotification(ct => BuildDrawNotificationAsync(outcome, ct));
    }

    public IO<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerTransientFailure> Expire(
        int giveawayId
    )
    {
        return CaptureDurable(ct => ExpireAsync(giveawayId, ct));
    }

    public IO<
        PointsGiveawayChangeNotificationCompleted,
        PointsGiveawaySchedulerNotificationFailure
    > NotifyChanged(int hostId)
    {
        return CaptureNotification(ct => NotifyChangedAsync(hostId, ct));
    }

    private async ValueTask<IReadOnlyList<PointsGiveawaySchedule>> LoadActiveAsync(
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await (
            from giveaway in db.PointsGiveaways.AsNoTracking()
            join host in db.Hosts.AsNoTracking() on giveaway.HostId equals host.Id
            where giveaway.Status == PointsGiveawayStatus.Active
            select new PointsGiveawaySchedule(
                giveaway.Id,
                giveaway.HostId,
                host.Login,
                giveaway.StartedAtUtc,
                giveaway.EndsAtUtc,
                null
            )
        ).ToListAsync(ct);
    }

    private async ValueTask<Option<string>> BuildUpdateAsync(
        int giveawayId,
        DateTime endsAtUtc,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var giveaway = await db
            .PointsGiveaways.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == giveawayId, ct);
        if (giveaway is null || giveaway.Status != PointsGiveawayStatus.Active)
        {
            return Option<string>.None;
        }

        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, giveaway.HostId, ct);
        var message = formatter.FormatUpdate(
            settings.GiveawayUpdateReply,
            settings,
            endsAtUtc - GetUtcNow()
        );
        return Message(message);
    }

    private async ValueTask<PointsGiveawayDrawOutcome> DrawAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        return await draws.DrawOutcomeAsync(giveawayId, ct);
    }

    private async ValueTask<Option<string>> BuildDrawNotificationAsync(
        PointsGiveawayDrawOutcome outcome,
        CancellationToken ct
    )
    {
        return await outcome.Match(
            _ => ValueTask.FromResult(FormattedMessage(new ReplyDeliveryMap())),
            notActive => FormatWithDeliveryAsync(notActive.Settings.HostId),
            noEntrants => FormatWithDeliveryAsync(noEntrants.Settings.HostId),
            payoutFailed => FormatWithDeliveryAsync(payoutFailed.Settings.HostId),
            winners => FormatWithDeliveryAsync(winners.Settings.HostId)
        );

        async ValueTask<Option<string>> FormatWithDeliveryAsync(int hostId)
        {
            return FormattedMessage(await LoadReplyDeliveryAsync(hostId, ct));
        }

        Option<string> FormattedMessage(ReplyDeliveryMap delivery)
        {
            return formatter
                .Reply(outcome, delivery)
                .Match(succeeded => Message(succeeded.Message), failed => Message(failed.Message));
        }
    }

    private async ValueTask<PointsGiveawayExpirationOutcome> ExpireAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        PointsGiveawayExpirationOutcome? committedOutcome = null;
        try
        {
            return await ExpireAndCommitAsync(
                giveawayId,
                outcome => committedOutcome = outcome,
                ct
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PointsGiveawayExpirationCommitAmbiguousException)
        {
            throw;
        }
        catch (Exception exception) when (committedOutcome is not null)
        {
            throw new PointsGiveawayExpirationPostCommitException(
                giveawayId,
                committedOutcome.Value,
                exception
            );
        }
    }

    private async ValueTask<PointsGiveawayExpirationOutcome> ExpireAndCommitAsync(
        int giveawayId,
        Action<PointsGiveawayExpirationOutcome> onCommitted,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var expired = await db
            .PointsGiveaways.Where(x =>
                x.Id == giveawayId && x.Status == PointsGiveawayStatus.Active
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(x => x.Status, PointsGiveawayStatus.Expired)
                        .SetProperty(x => x.CompletedAtUtc, GetUtcNow()),
                ct
            );

        if (expired == 0)
        {
            return PointsGiveawayExpirationOutcome.AlreadyInactive;
        }

        await CommitExpirationAsync(transaction, giveawayId, ct);
        onCommitted(PointsGiveawayExpirationOutcome.Expired);
        return PointsGiveawayExpirationOutcome.Expired;
    }

    private async ValueTask<PointsGiveawayChangeNotificationCompleted> NotifyChangedAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await changeNotification.NotifyAsync(hostId, ct);
        return new PointsGiveawayChangeNotificationCompleted();
    }

    private static IO<TValue, PointsGiveawaySchedulerTransientFailure> CaptureDurable<TValue>(
        Func<CancellationToken, ValueTask<TValue>> operation
    )
    {
        return IO<TValue, PointsGiveawaySchedulerTransientFailure>.Create(async ct =>
        {
            try
            {
                return Result<TValue, PointsGiveawaySchedulerTransientFailure>.Success(
                    await operation(ct)
                );
            }
            catch (Exception exception)
                when (PointsGiveawaySchedulerFailureClassifier.IsTransient(exception))
            {
                ct.ThrowIfCancellationRequested();
                return Result<TValue, PointsGiveawaySchedulerTransientFailure>.Error(
                    new PointsGiveawaySchedulerTransientFailure(exception)
                );
            }
        });
    }

    private static IO<
        TValue,
        PointsGiveawaySchedulerNotificationFailure
    > CaptureNotification<TValue>(Func<CancellationToken, ValueTask<TValue>> operation)
    {
        return IO<TValue, PointsGiveawaySchedulerNotificationFailure>.Create(async ct =>
        {
            try
            {
                return Result<TValue, PointsGiveawaySchedulerNotificationFailure>.Success(
                    await operation(ct)
                );
            }
            catch (Exception exception)
                when (PointsGiveawaySchedulerFailureClassifier.IsNotificationFailure(exception))
            {
                ct.ThrowIfCancellationRequested();
                return Result<TValue, PointsGiveawaySchedulerNotificationFailure>.Error(
                    new PointsGiveawaySchedulerNotificationFailure(exception)
                );
            }
        });
    }

    private static Option<string> Message(string? message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? Option<string>.None
            : Option<string>.Some(message);
    }

    private DateTime GetUtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }

    private async Task<ReplyDeliveryMap> LoadReplyDeliveryAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PointsGiveawayQueries.LoadReplyDeliveryAsync(db, hostId, ct);
    }

    private static async Task CommitExpirationAsync(
        IDbContextTransaction transaction,
        int giveawayId,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            throw new PointsGiveawayExpirationCommitAmbiguousException(giveawayId, exception);
        }
    }
}

internal sealed record PointsGiveawaySchedulerTransientFailure(Exception Cause);

internal sealed record PointsGiveawaySchedulerNotificationFailure(Exception Cause);

internal sealed class PointsGiveawayExpirationCommitAmbiguousException(
    int giveawayId,
    Exception innerException
) : Exception("The points giveaway expiration commit outcome is ambiguous.", innerException)
{
    internal int GiveawayId { get; } = giveawayId;

    internal PointsGiveawayExpirationOutcome IntendedOutcome =>
        PointsGiveawayExpirationOutcome.Expired;
}

internal sealed class PointsGiveawayExpirationPostCommitException(
    int giveawayId,
    PointsGiveawayExpirationOutcome committedOutcome,
    Exception innerException
) : Exception("The committed points giveaway expiration cleanup failed.", innerException)
{
    internal int GiveawayId { get; } = giveawayId;

    internal PointsGiveawayExpirationOutcome CommittedOutcome { get; } = committedOutcome;
}

internal enum PointsGiveawayExpirationOutcome
{
    Expired,
    AlreadyInactive,
}
