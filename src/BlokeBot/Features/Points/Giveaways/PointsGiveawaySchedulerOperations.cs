using BlokeBot.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

internal interface IPointsGiveawaySchedulerOperations
{
    IO<IReadOnlyList<PointsGiveawaySchedule>, PointsGiveawaySchedulerOperationFailure> LoadActive();

    IO<Option<string>, PointsGiveawaySchedulerOperationFailure> BuildUpdate(
        int giveawayId,
        DateTime endsAtUtc
    );

    IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerOperationFailure> Draw(
        int giveawayId
    );

    IO<Option<string>, PointsGiveawaySchedulerOperationFailure> BuildDrawNotification(
        PointsGiveawayDrawOutcome outcome
    );

    IO<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerOperationFailure> Expire(
        int giveawayId
    );
}

internal sealed class PointsGiveawaySchedulerOperations(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointsGiveawayDrawService draws,
    PointsGiveawayMessageFormatter formatter,
    PointsChangeNotifier changes,
    TimeProvider timeProvider
) : IPointsGiveawaySchedulerOperations
{
    public IO<
        IReadOnlyList<PointsGiveawaySchedule>,
        PointsGiveawaySchedulerOperationFailure
    > LoadActive() => Capture(LoadActiveAsync);

    public IO<Option<string>, PointsGiveawaySchedulerOperationFailure> BuildUpdate(
        int giveawayId,
        DateTime endsAtUtc
    ) => Capture(ct => BuildUpdateAsync(giveawayId, endsAtUtc, ct));

    public IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerOperationFailure> Draw(
        int giveawayId
    ) =>
        Capture(ct => DrawAsync(giveawayId, ct));

    public IO<Option<string>, PointsGiveawaySchedulerOperationFailure> BuildDrawNotification(
        PointsGiveawayDrawOutcome outcome
    ) => Capture(ct => BuildDrawNotificationAsync(outcome, ct));

    public IO<
        PointsGiveawayExpirationOutcome,
        PointsGiveawaySchedulerOperationFailure
    > Expire(int giveawayId) => Capture(ct => ExpireAsync(giveawayId, ct));

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
            return Option<string>.None;

        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, giveaway.HostId, ct);
        var message = formatter.Format(
            settings.GiveawayUpdateReply,
            settings,
            timeLeft: formatter.FormatTimeLeft(endsAtUtc - GetUtcNow())
        );
        return Message(message);
    }

    private async ValueTask<PointsGiveawayDrawOutcome> DrawAsync(
        int giveawayId,
        CancellationToken ct
    ) => await draws.DrawOutcomeAsync(giveawayId, ct);

    private async ValueTask<Option<string>> BuildDrawNotificationAsync(
        PointsGiveawayDrawOutcome outcome,
        CancellationToken ct
    )
    {
        var delivery = outcome.Settings is { } settings
            ? await LoadReplyDeliveryAsync(settings.HostId, ct)
            : new ReplyDeliveryMap();
        return Message(formatter.Reply(outcome, delivery).Message);
    }

    private async ValueTask<PointsGiveawayExpirationOutcome> ExpireAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
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
            return PointsGiveawayExpirationOutcome.AlreadyInactive;

        await changes.NotifyChangedAsync(ct);
        return PointsGiveawayExpirationOutcome.Expired;
    }

    private static IO<TValue, PointsGiveawaySchedulerOperationFailure> Capture<TValue>(
        Func<CancellationToken, ValueTask<TValue>> operation
    ) =>
        IO<TValue, PointsGiveawaySchedulerOperationFailure>.FromException<Exception>(
            operation,
            exception => new PointsGiveawaySchedulerOperationFailure(exception)
        );

    private static Option<string> Message(string? message) =>
        string.IsNullOrWhiteSpace(message)
            ? Option<string>.None
            : Option<string>.Some(message);

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<ReplyDeliveryMap> LoadReplyDeliveryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PointsGiveawayQueries.LoadReplyDeliveryAsync(db, hostId, ct);
    }
}

internal sealed record PointsGiveawaySchedulerOperationFailure(Exception Cause);

internal enum PointsGiveawayExpirationOutcome
{
    Expired,
    AlreadyInactive,
}
