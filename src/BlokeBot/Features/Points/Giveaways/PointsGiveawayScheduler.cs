using Alsi.TwitchBot;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Points.Giveaways;

internal sealed class PointsGiveawayScheduler(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<PointsGiveawayScheduler> log
) : BackgroundService, IPointsGiveawayScheduler
{
    private static readonly double[] ReminderFactors = [0.25, 0.5, 0.75];

    private readonly object scheduleGate = new();
    private readonly Dictionary<int, ScheduledGiveaway> schedules = [];
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
        scheduled.Task = RunScheduleAsync(schedule, cts);
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var activeGiveaways = await (
            from giveaway in db.PointsGiveaways.AsNoTracking()
            join host in db.Hosts.AsNoTracking() on giveaway.HostId equals host.Id
            where giveaway.Status == PointsGiveawayStatus.Active
            select new
            {
                giveaway.Id,
                giveaway.HostId,
                HostLogin = host.Login,
                giveaway.StartedAtUtc,
                giveaway.EndsAtUtc,
            }
        ).ToListAsync(ct);

        var now = GetUtcNow();
        foreach (var giveaway in activeGiveaways)
        {
            var schedule = new PointsGiveawaySchedule(
                giveaway.Id,
                giveaway.HostId,
                giveaway.HostLogin,
                giveaway.StartedAtUtc,
                giveaway.EndsAtUtc,
                null
            );

            if (giveaway.EndsAtUtc <= now)
            {
                await ExpireOverdueAsync(schedule, ct);
                continue;
            }

            Schedule(schedule);
        }
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
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to rehydrate active points giveaways.");
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunScheduleAsync(
        PointsGiveawaySchedule schedule,
        CancellationTokenSource cts
    )
    {
        try
        {
            var token = cts.Token;
            if (schedule.EndsAtUtc <= GetUtcNow())
            {
                await ExpireOverdueAsync(schedule, token);
                return;
            }

            foreach (var reminderAtUtc in ReminderTimes(schedule))
            {
                if (reminderAtUtc <= GetUtcNow())
                    continue;

                await DelayUntilAsync(reminderAtUtc, token);
                var message = await Giveaways().BuildUpdateMessageAsync(
                    schedule.GiveawayId,
                    schedule.EndsAtUtc,
                    token
                );
                if (!string.IsNullOrWhiteSpace(message))
                    await SendMessageAsync(schedule, message, token);
            }

            await DelayUntilAsync(schedule.EndsAtUtc, token);
            var result = await Giveaways().DrawAsync(schedule.GiveawayId, token);
            if (!string.IsNullOrWhiteSpace(result.Message))
                await SendMessageAsync(schedule, result.Message, token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "Points giveaway scheduler failed for giveaway {GiveawayId}.",
                schedule.GiveawayId
            );
        }
        finally
        {
            RemoveCompleted(schedule.GiveawayId, cts);
            cts.Dispose();
        }
    }

    private async Task ExpireOverdueAsync(PointsGiveawaySchedule schedule, CancellationToken ct)
    {
        try
        {
            var expired = await Giveaways().ExpireAsync(schedule.GiveawayId, ct);
            if (expired)
            {
                log.LogInformation(
                    "Expired overdue points giveaway {GiveawayId} for host {HostId}.",
                    schedule.GiveawayId,
                    schedule.HostId
                );
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "Failed to expire overdue points giveaway {GiveawayId}.",
                schedule.GiveawayId
            );
        }
    }

    private async Task DelayUntilAsync(DateTime targetUtc, CancellationToken ct)
    {
        var delay = targetUtc - GetUtcNow();
        if (delay <= TimeSpan.Zero)
            return;

        await Task.Delay(delay, timeProvider, ct);
    }

    private async Task SendMessageAsync(
        PointsGiveawaySchedule schedule,
        string message,
        CancellationToken ct
    )
    {
        if (schedule.Reply is not null)
        {
            await schedule.Reply(message, ct);
            return;
        }

        var sender = services.GetService<ITwitchChatMessageSender>();
        if (sender is not null)
            await sender.SendAsync(schedule.HostLogin, message, ct);
    }

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

    private PointsGiveawayService Giveaways() => services.GetRequiredService<PointsGiveawayService>();

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;

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
}
