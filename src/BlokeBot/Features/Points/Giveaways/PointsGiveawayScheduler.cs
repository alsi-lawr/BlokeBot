using System.Diagnostics;
using System.Threading.Channels;
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
    private static readonly double[] _reminderFactors = [0.25, 0.5, 0.75];

    private readonly object _scheduleGate = new();
    private readonly Dictionary<int, ScheduledGiveaway> _schedules = [];
    private readonly TimeSpan _retryDelay = ValidRetryDelay(recoveryPolicy);
    private readonly Channel<PointsGiveawaySchedulerUnhealthyReport> _unhealthyReports =
        Channel.CreateUnbounded<PointsGiveawaySchedulerUnhealthyReport>();
    private CancellationToken _shutdownToken;

    public void Schedule(PointsGiveawaySchedule schedule)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);
        var scheduled = new ScheduledGiveaway(cts);
        ScheduledGiveaway? previous;

        lock (_scheduleGate)
        {
            _schedules.Remove(schedule.GiveawayId, out previous);
            _schedules[schedule.GiveawayId] = scheduled;
        }

        previous?.Cancellation.Cancel();
        scheduled.Task = RunScheduledAsync(schedule, cts);
    }

    public void Cancel(int giveawayId)
    {
        ScheduledGiveaway? scheduled;
        lock (_scheduleGate)
        {
            if (!_schedules.Remove(giveawayId, out scheduled))
            {
                return;
            }
        }

        scheduled.Cancellation.Cancel();
    }

    internal bool IsScheduled(int giveawayId)
    {
        lock (_scheduleGate)
        {
            return _schedules.ContainsKey(giveawayId);
        }
    }

    internal async Task RehydrateAsync(CancellationToken ct)
    {
        try
        {
            await RehydrateCoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PointsGiveawaySchedulerUnhealthyException exception)
        {
            LogUnhealthy(exception.Report);
            throw;
        }
        catch (Exception exception)
        {
            var report = new PointsGiveawaySchedulerUnhealthyReport.Rehydration
            {
                Classification = PointsGiveawaySchedulerFailureClassifier.ClassifyUnhealthy(
                    exception
                ),
                Cause = exception,
            };
            LogUnhealthy(report);
            throw new PointsGiveawaySchedulerUnhealthyException(report);
        }
    }

    private async Task RehydrateCoreAsync(CancellationToken ct)
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
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _shutdownToken = stoppingToken;

        try
        {
            await RehydrateAsync(stoppingToken);
            await ThrowWhenUnhealthyAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task ThrowWhenUnhealthyAsync(CancellationToken ct)
    {
        var report = await _unhealthyReports.Reader.ReadAsync(ct);
        throw new PointsGiveawaySchedulerUnhealthyException(report);
    }

    internal async Task ExecuteScheduleAsync(PointsGiveawaySchedule schedule, CancellationToken ct)
    {
        if (schedule.EndsAtUtc <= GetUtcNow())
        {
            await ExpireOverdueWithRecoveryAsync(schedule, ct);
            return;
        }

        foreach (var reminderAtUtc in ReminderTimes(schedule))
        {
            if (reminderAtUtc <= GetUtcNow())
            {
                continue;
            }

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
        await drawOutcome.Match(
            static _ => Task.CompletedTask,
            static _ => Task.CompletedTask,
            _ => NotifyChangedAsync(schedule.GiveawayId, ct),
            _ => NotifyChangedAsync(schedule.GiveawayId, ct)
        );

        var drawMessage = await BuildDrawNotificationAsync(schedule, drawOutcome, ct);
        await SendAsync(schedule, drawMessage, PointsGiveawayNotificationKind.DrawResult, ct);
    }

    private async Task RunScheduledAsync(
        PointsGiveawaySchedule schedule,
        CancellationTokenSource cts
    )
    {
        try
        {
            await ExecuteScheduleAsync(schedule, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (PointsGiveawaySchedulerUnhealthyException exception)
        {
            PublishUnhealthy(exception.Report);
        }
        catch (Exception exception)
        {
            PublishUnhealthy(
                GiveawayUnhealthyReport(
                    PointsGiveawaySchedulerOperation.Schedule,
                    schedule.GiveawayId,
                    exception
                )
            );
        }
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
        try
        {
            var operation = operations.LoadActive();
            var attempt = 1;
            while (true)
            {
                var result = ToAttempt(await operation.ExecuteAsync(ct));
                switch (result)
                {
                    case OperationAttempt<
                        IReadOnlyList<PointsGiveawaySchedule>,
                        PointsGiveawaySchedulerTransientFailure
                    >.Succeeded success:
                        if (attempt > 1)
                        {
                            log.LogInformation(
                                "Points giveaway scheduler rehydration recovered on attempt {Attempt}.",
                                attempt
                            );
                        }

                        return success.Value;
                    case OperationAttempt<
                        IReadOnlyList<PointsGiveawaySchedule>,
                        PointsGiveawaySchedulerTransientFailure
                    >.Failed failure:
                        ReportRehydrationRetryScheduled(attempt++, failure.Failure.Cause);
                        await DelayForRecoveryAsync(ct);
                        break;
                    default:
                        throw new UnreachableException("Unknown scheduler operation attempt.");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var report = new PointsGiveawaySchedulerUnhealthyReport.Rehydration
            {
                Classification = PointsGiveawaySchedulerFailureClassifier.ClassifyUnhealthy(
                    exception
                ),
                Cause = exception,
            };
            throw new PointsGiveawaySchedulerUnhealthyException(report);
        }
    }

    private async Task<TValue> ExecuteGiveawayOperationWithRecoveryAsync<TValue>(
        IO<TValue, PointsGiveawaySchedulerTransientFailure> operation,
        PointsGiveawaySchedulerOperation operationKind,
        int giveawayId,
        CancellationToken ct
    )
    {
        try
        {
            var attempt = 1;
            while (true)
            {
                var result = ToAttempt(await operation.ExecuteAsync(ct));
                switch (result)
                {
                    case OperationAttempt<
                        TValue,
                        PointsGiveawaySchedulerTransientFailure
                    >.Succeeded success:
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
                    case OperationAttempt<
                        TValue,
                        PointsGiveawaySchedulerTransientFailure
                    >.Failed failure:
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PointsGiveawaySchedulerUnhealthyException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PointsGiveawaySchedulerUnhealthyException(
                GiveawayUnhealthyReport(operationKind, giveawayId, exception)
            );
        }
    }

    private async Task<Option<string>> BuildUpdateAsync(
        PointsGiveawaySchedule schedule,
        CancellationToken ct
    )
    {
        var result = ToAttempt(
            await operations.BuildUpdate(schedule.GiveawayId, schedule.EndsAtUtc).ExecuteAsync(ct)
        );
        switch (result)
        {
            case OperationAttempt<
                Option<string>,
                PointsGiveawaySchedulerNotificationFailure
            >.Succeeded success:
                return success.Value;
            case OperationAttempt<
                Option<string>,
                PointsGiveawaySchedulerNotificationFailure
            >.Failed failure:
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
        var result = ToAttempt(await operations.BuildDrawNotification(outcome).ExecuteAsync(ct));
        switch (result)
        {
            case OperationAttempt<
                Option<string>,
                PointsGiveawaySchedulerNotificationFailure
            >.Succeeded success:
                return success.Value;
            case OperationAttempt<
                Option<string>,
                PointsGiveawaySchedulerNotificationFailure
            >.Failed failure:
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

    private async Task NotifyChangedAsync(int giveawayId, CancellationToken ct)
    {
        var result = ToAttempt(await operations.NotifyChanged().ExecuteAsync(ct));
        switch (result)
        {
            case OperationAttempt<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >.Succeeded:
                return;
            case OperationAttempt<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >.Failed failure:
                ReportNotificationFailure(
                    giveawayId,
                    PointsGiveawayNotificationKind.StateChanged,
                    failure.Failure.Cause
                );
                return;
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
                await NotifyChangedAsync(schedule.GiveawayId, ct);
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
        await message.Match(Send, static () => ValueTask.CompletedTask);
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
                when (PointsGiveawaySchedulerFailureClassifier.IsNotificationFailure(exception))
            {
                ReportNotificationFailure(schedule.GiveawayId, kind, exception);
            }
        }
    }

    private async Task DelayUntilAsync(DateTime targetUtc, CancellationToken ct)
    {
        var delay = targetUtc - GetUtcNow();
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, timeProvider, ct);
    }

    private Task DelayForRecoveryAsync(CancellationToken ct)
    {
        return Task.Delay(_retryDelay, timeProvider, ct);
    }

    private void ReportRehydrationRetryScheduled(int attempt, Exception exception)
    {
        log.LogError(
            "Points giveaway scheduler rehydration failed with {FailureType} on attempt {Attempt}; retry scheduled for {RetryAtUtc}.",
            exception.GetType().FullName,
            attempt,
            GetUtcNow().Add(_retryDelay)
        );
    }

    private void ReportRetryScheduled(
        PointsGiveawaySchedulerOperation operation,
        int giveawayId,
        int attempt,
        Exception exception
    )
    {
        log.LogError(
            "Points giveaway scheduler {Operation} failed for giveaway {GiveawayId} with {FailureType} on attempt {Attempt}; retry scheduled for {RetryAtUtc}.",
            operation,
            giveawayId,
            exception.GetType().FullName,
            attempt,
            GetUtcNow().Add(_retryDelay)
        );
    }

    private void ReportNotificationFailure(
        int giveawayId,
        PointsGiveawayNotificationKind kind,
        Exception exception
    )
    {
        log.LogError(
            "Points giveaway {NotificationKind} notification failed for giveaway {GiveawayId} with {FailureType}; delivery is not retried because acceptance is ambiguous, and durable schedule processing continues.",
            kind,
            giveawayId,
            exception.GetType().FullName
        );
    }

    private Task[] CancelAll()
    {
        ScheduledGiveaway[] scheduled;
        lock (_scheduleGate)
        {
            scheduled = _schedules.Values.ToArray();
            _schedules.Clear();
        }

        foreach (var giveaway in scheduled)
        {
            giveaway.Cancellation.Cancel();
        }

        return scheduled.Select(x => x.Task).ToArray();
    }

    private void RemoveCompleted(int giveawayId, CancellationTokenSource cts)
    {
        lock (_scheduleGate)
        {
            if (
                _schedules.TryGetValue(giveawayId, out var scheduled)
                && ReferenceEquals(scheduled.Cancellation, cts)
            )
            {
                _schedules.Remove(giveawayId);
            }
        }
    }

    private DateTime GetUtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }

    private static TimeSpan ValidRetryDelay(PointsGiveawaySchedulerRecoveryPolicy policy)
    {
        policy.EnsureValid();
        return policy.RetryDelay;
    }

    private void PublishUnhealthy(PointsGiveawaySchedulerUnhealthyReport report)
    {
        if (!_unhealthyReports.Writer.TryWrite(report))
        {
            throw new UnreachableException("The scheduler unhealthy report could not be queued.");
        }

        LogUnhealthy(report);
    }

    private void LogUnhealthy(PointsGiveawaySchedulerUnhealthyReport report)
    {
        switch (report)
        {
            case PointsGiveawaySchedulerUnhealthyReport.Rehydration rehydration:
                log.LogCritical(
                    "Points giveaway scheduler rehydration is unhealthy after a {Classification} failure ({FailureType}); the hosted scheduler will stop.",
                    rehydration.Classification,
                    rehydration.FailureType.FullName
                );
                return;
            case PointsGiveawaySchedulerUnhealthyReport.Giveaway giveaway:
                log.LogCritical(
                    "Points giveaway scheduler {Operation} is unhealthy for giveaway {GiveawayId} after a {Classification} failure ({FailureType}); the hosted scheduler will stop.",
                    giveaway.Operation,
                    giveaway.GiveawayId,
                    giveaway.Classification,
                    giveaway.FailureType.FullName
                );
                return;
            default:
                throw new UnreachableException("Unknown giveaway scheduler unhealthy report.");
        }
    }

    private static PointsGiveawaySchedulerUnhealthyReport.Giveaway GiveawayUnhealthyReport(
        PointsGiveawaySchedulerOperation operation,
        int giveawayId,
        Exception exception
    )
    {
        return new()
        {
            Operation = operation,
            GiveawayId = giveawayId,
            Classification = PointsGiveawaySchedulerFailureClassifier.ClassifyUnhealthy(exception),
            Cause = exception,
        };
    }

    private static OperationAttempt<TValue, TError> ToAttempt<TValue, TError>(
        Result<TValue, TError> result
    )
    {
        return result.Match<OperationAttempt<TValue, TError>>(
            value => new OperationAttempt<TValue, TError>.Succeeded(value),
            failure => new OperationAttempt<TValue, TError>.Failed(failure)
        );
    }

    private static IEnumerable<DateTime> ReminderTimes(PointsGiveawaySchedule schedule)
    {
        var duration = schedule.EndsAtUtc - schedule.StartedAtUtc;
        foreach (var factor in _reminderFactors)
        {
            yield return schedule.StartedAtUtc.Add(
                TimeSpan.FromTicks((long)(duration.Ticks * factor))
            );
        }
    }

    private sealed class ScheduledGiveaway(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } = Task.CompletedTask;
    }

    private abstract record OperationAttempt<TValue, TError>
    {
        private OperationAttempt() { }

        internal sealed record Succeeded(TValue Value) : OperationAttempt<TValue, TError>;

        internal sealed record Failed(TError Failure) : OperationAttempt<TValue, TError>;
    }
}

internal enum PointsGiveawayNotificationKind
{
    Reminder,
    DrawResult,
    StateChanged,
}
