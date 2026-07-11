using System.Diagnostics;
using BlokeBot.Functional;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Points.Giveaways;

internal sealed class PointsGiveawayScheduler(
    IPointsGiveawaySchedulerOperations operations,
    IPointsGiveawaySchedulerNotification notification,
    PointsGiveawaySchedulerRecoveryPolicy recoveryPolicy,
    TimeProvider timeProvider,
    ILogger<PointsGiveawayScheduler> log
) : BackgroundService, IPointsGiveawayScheduler
{
    private static readonly double[] ReminderFactors = [0.25, 0.5, 0.75];

    private readonly object scheduleGate = new();
    private readonly Dictionary<int, ScheduledGiveaway> schedules = [];
    private readonly TimeSpan retryDelay = ValidRetryDelay(recoveryPolicy);
    private CancellationToken shutdownToken;

    public void Schedule(PointsGiveawaySchedule schedule)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        var scheduled = new ScheduledGiveaway(cts);
        ScheduledGiveaway? previous;

        lock (scheduleGate)
        {
            schedules.Remove(schedule.GiveawayId, out previous);
            schedules[schedule.GiveawayId] = scheduled;
        }

        previous?.Cancellation.Cancel();
        scheduled.Task = RunScheduledAsync(schedule, cts);
    }

    public void Cancel(int giveawayId)
    {
        ScheduledGiveaway? scheduled;
        lock (scheduleGate)
        {
            if (!schedules.Remove(giveawayId, out scheduled))
                return;
        }

        scheduled.Cancellation.Cancel();
    }

    internal bool IsScheduled(int giveawayId)
    {
        lock (scheduleGate)
        {
            return schedules.ContainsKey(giveawayId);
        }
    }

    internal async Task RehydrateAsync(CancellationToken ct)
    {
        var activeGiveaways = await LoadActiveWithRecoveryAsync(ct);
        var now = GetUtcNow();
        var overdueExpirations = new List<Task>();
        foreach (var schedule in activeGiveaways)
        {
            if (schedule.EndsAtUtc <= now)
            {
                overdueExpirations.Add(ExpireOverdueWithRecoveryAsync(schedule, ct));
                continue;
            }

            Schedule(schedule);
        }

        await Task.WhenAll(overdueExpirations);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var tasks = CancelAll();
        await base.StopAsync(cancellationToken);

        if (tasks.Length == 0)
            return;

        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        shutdownToken = stoppingToken;

        try
        {
            await RehydrateAsync(stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task ExecuteScheduleAsync(
        PointsGiveawaySchedule schedule,
        CancellationToken ct
    )
    {
        if (schedule.EndsAtUtc <= GetUtcNow())
        {
            await ExpireOverdueWithRecoveryAsync(schedule, ct);
            return;
        }

        foreach (var reminderAtUtc in ReminderTimes(schedule))
        {
            if (reminderAtUtc <= GetUtcNow())
                continue;

            await DelayUntilAsync(reminderAtUtc, ct);
            var message = await BuildUpdateAsync(schedule, ct);
            await SendAsync(schedule, message, PointsGiveawayNotificationKind.Reminder, ct);
        }

        await DelayUntilAsync(schedule.EndsAtUtc, ct);
        var drawOutcome = await ExecuteGiveawayOperationWithRecoveryAsync(
            operations.Draw(schedule.GiveawayId),
            PointsGiveawaySchedulerOperation.Draw,
            schedule.GiveawayId,
            ct
        );
        var drawMessage = await BuildDrawNotificationAsync(schedule, drawOutcome, ct);
        await SendAsync(
            schedule,
            drawMessage,
            PointsGiveawayNotificationKind.DrawResult,
            ct
        );
    }

    private async Task RunScheduledAsync(
        PointsGiveawaySchedule schedule,
        CancellationTokenSource cts
    )
    {
        var unexpectedAttempt = 1;
        try
        {
            while (true)
            {
                try
                {
                    await ExecuteScheduleAsync(schedule, cts.Token);
                    return;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    ReportRetryScheduled(
                        PointsGiveawaySchedulerOperation.Schedule,
                        schedule.GiveawayId,
                        unexpectedAttempt++,
                        exception
                    );
                    await DelayForRecoveryAsync(cts.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            RemoveCompleted(schedule.GiveawayId, cts);
            cts.Dispose();
        }
    }

    private async Task<IReadOnlyList<PointsGiveawaySchedule>> LoadActiveWithRecoveryAsync(
        CancellationToken ct
    )
    {
        var operation = operations.LoadActive();
        var attempt = 1;
        while (true)
        {
            var result = ToAttempt(await operation.ExecuteAsync(ct));
            switch (result)
            {
                case OperationAttempt<IReadOnlyList<PointsGiveawaySchedule>>.Succeeded success:
                    if (attempt > 1)
                    {
                        log.LogInformation(
                            "Points giveaway scheduler rehydration recovered on attempt {Attempt}.",
                            attempt
                        );
                    }

                    return success.Value;
                case OperationAttempt<IReadOnlyList<PointsGiveawaySchedule>>.Failed failure:
                    ReportRehydrationRetryScheduled(attempt++, failure.Failure.Cause);
                    await DelayForRecoveryAsync(ct);
                    break;
                default:
                    throw new UnreachableException("Unknown scheduler operation attempt.");
            }
        }
    }

    private async Task<TValue> ExecuteGiveawayOperationWithRecoveryAsync<TValue>(
        IO<TValue, PointsGiveawaySchedulerOperationFailure> operation,
        PointsGiveawaySchedulerOperation operationKind,
        int giveawayId,
        CancellationToken ct
    )
    {
        var attempt = 1;
        while (true)
        {
            var result = ToAttempt(await operation.ExecuteAsync(ct));
            switch (result)
            {
                case OperationAttempt<TValue>.Succeeded success:
                    if (attempt > 1)
                    {
                        log.LogInformation(
                            "Points giveaway scheduler {Operation} recovered for giveaway {GiveawayId} on attempt {Attempt}.",
                            operationKind,
                            giveawayId,
                            attempt
                        );
                    }

                    return success.Value;
                case OperationAttempt<TValue>.Failed failure:
                    ReportRetryScheduled(
                        operationKind,
                        giveawayId,
                        attempt++,
                        failure.Failure.Cause
                    );
                    await DelayForRecoveryAsync(ct);
                    break;
                default:
                    throw new UnreachableException("Unknown scheduler operation attempt.");
            }
        }
    }

    private async Task<Option<string>> BuildUpdateAsync(
        PointsGiveawaySchedule schedule,
        CancellationToken ct
    )
    {
        var result = ToAttempt(
            await operations
                .BuildUpdate(schedule.GiveawayId, schedule.EndsAtUtc)
                .ExecuteAsync(ct)
        );
        switch (result)
        {
            case OperationAttempt<Option<string>>.Succeeded success:
                return success.Value;
            case OperationAttempt<Option<string>>.Failed failure:
                ReportNotificationFailure(
                    schedule.GiveawayId,
                    PointsGiveawayNotificationKind.Reminder,
                    failure.Failure.Cause
                );
                return Option<string>.None;
            default:
                throw new UnreachableException("Unknown scheduler operation attempt.");
        }
    }

    private async Task<Option<string>> BuildDrawNotificationAsync(
        PointsGiveawaySchedule schedule,
        PointsGiveawayDrawOutcome outcome,
        CancellationToken ct
    )
    {
        var result = ToAttempt(
            await operations.BuildDrawNotification(outcome).ExecuteAsync(ct)
        );
        switch (result)
        {
            case OperationAttempt<Option<string>>.Succeeded success:
                return success.Value;
            case OperationAttempt<Option<string>>.Failed failure:
                ReportNotificationFailure(
                    schedule.GiveawayId,
                    PointsGiveawayNotificationKind.DrawResult,
                    failure.Failure.Cause
                );
                return Option<string>.None;
            default:
                throw new UnreachableException("Unknown scheduler operation attempt.");
        }
    }

    private async Task ExpireOverdueWithRecoveryAsync(
        PointsGiveawaySchedule schedule,
        CancellationToken ct
    )
    {
        var outcome = await ExecuteGiveawayOperationWithRecoveryAsync(
            operations.Expire(schedule.GiveawayId),
            PointsGiveawaySchedulerOperation.Expire,
            schedule.GiveawayId,
            ct
        );
        switch (outcome)
        {
            case PointsGiveawayExpirationOutcome.Expired:
                log.LogInformation(
                    "Expired overdue points giveaway {GiveawayId} for host {HostId}.",
                    schedule.GiveawayId,
                    schedule.HostId
                );
                return;
            case PointsGiveawayExpirationOutcome.AlreadyInactive:
                return;
            default:
                throw new UnreachableException("Unknown giveaway expiration outcome.");
        }
    }

    private async ValueTask SendAsync(
        PointsGiveawaySchedule schedule,
        Option<string> message,
        PointsGiveawayNotificationKind kind,
        CancellationToken ct
    )
    {
        await message.Match(
            Send,
            static () => ValueTask.CompletedTask
        );
        return;

        async ValueTask Send(string value)
        {
            try
            {
                await notification.SendAsync(schedule, value, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ReportNotificationFailure(schedule.GiveawayId, kind, exception);
            }
        }
    }

    private async Task DelayUntilAsync(DateTime targetUtc, CancellationToken ct)
    {
        var delay = targetUtc - GetUtcNow();
        if (delay <= TimeSpan.Zero)
            return;

        await Task.Delay(delay, timeProvider, ct);
    }

    private Task DelayForRecoveryAsync(CancellationToken ct) =>
        Task.Delay(retryDelay, timeProvider, ct);

    private void ReportRehydrationRetryScheduled(int attempt, Exception exception) =>
        log.LogError(
            "Points giveaway scheduler rehydration failed with {FailureType} on attempt {Attempt}; retry scheduled for {RetryAtUtc}.",
            exception.GetType().FullName,
            attempt,
            GetUtcNow().Add(retryDelay)
        );

    private void ReportRetryScheduled(
        PointsGiveawaySchedulerOperation operation,
        int giveawayId,
        int attempt,
        Exception exception
    ) =>
        log.LogError(
            "Points giveaway scheduler {Operation} failed for giveaway {GiveawayId} with {FailureType} on attempt {Attempt}; retry scheduled for {RetryAtUtc}.",
            operation,
            giveawayId,
            exception.GetType().FullName,
            attempt,
            GetUtcNow().Add(retryDelay)
        );

    private void ReportNotificationFailure(
        int giveawayId,
        PointsGiveawayNotificationKind kind,
        Exception exception
    ) =>
        log.LogError(
            "Points giveaway {NotificationKind} notification failed for giveaway {GiveawayId} with {FailureType}; delivery is not retried because acceptance is ambiguous, and durable schedule processing continues.",
            kind,
            giveawayId,
            exception.GetType().FullName
        );

    private Task[] CancelAll()
    {
        ScheduledGiveaway[] scheduled;
        lock (scheduleGate)
        {
            scheduled = schedules.Values.ToArray();
            schedules.Clear();
        }

        foreach (var giveaway in scheduled)
            giveaway.Cancellation.Cancel();

        return scheduled.Select(x => x.Task).ToArray();
    }

    private void RemoveCompleted(int giveawayId, CancellationTokenSource cts)
    {
        lock (scheduleGate)
        {
            if (
                schedules.TryGetValue(giveawayId, out var scheduled)
                && ReferenceEquals(scheduled.Cancellation, cts)
            )
            {
                schedules.Remove(giveawayId);
            }
        }
    }

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static TimeSpan ValidRetryDelay(PointsGiveawaySchedulerRecoveryPolicy policy)
    {
        policy.EnsureValid();
        return policy.RetryDelay;
    }

    private static OperationAttempt<TValue> ToAttempt<TValue>(
        Result<TValue, PointsGiveawaySchedulerOperationFailure> result
    ) =>
        result.Match<OperationAttempt<TValue>>(
            value => new OperationAttempt<TValue>.Succeeded(value),
            failure => new OperationAttempt<TValue>.Failed(failure)
        );

    private static IEnumerable<DateTime> ReminderTimes(PointsGiveawaySchedule schedule)
    {
        var duration = schedule.EndsAtUtc - schedule.StartedAtUtc;
        foreach (var factor in ReminderFactors)
            yield return schedule.StartedAtUtc.Add(
                TimeSpan.FromTicks((long)(duration.Ticks * factor))
            );
    }

    private sealed class ScheduledGiveaway(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } = Task.CompletedTask;
    }

    private abstract record OperationAttempt<TValue>
    {
        private OperationAttempt() { }

        internal sealed record Succeeded(TValue Value) : OperationAttempt<TValue>;

        internal sealed record Failed(PointsGiveawaySchedulerOperationFailure Failure)
            : OperationAttempt<TValue>;
    }
}

internal enum PointsGiveawaySchedulerOperation
{
    Schedule,
    Draw,
    Expire,
}

internal enum PointsGiveawayNotificationKind
{
    Reminder,
    DrawResult,
}
