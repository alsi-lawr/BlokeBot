using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.CustomCommands;

internal sealed class CustomAnnouncementScheduler(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ICustomAnnouncementSender sender,
    ICustomAnnouncementTickScheduler scheduler,
    CustomMessageSelector messageSelector,
    IOptions<BlokeBotOptions> options,
    ILogger<CustomAnnouncementScheduler> log
) : BackgroundService
{
    internal async Task RunTickAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = scheduler.GetUtcNow().UtcDateTime;
        var candidates = await (
            from announcement in db.CustomAnnouncements.AsNoTracking()
            join schedule in db.CustomAnnouncementSchedules.AsNoTracking()
                on new { announcement.HostId, CustomAnnouncementId = announcement.Id } equals new
                {
                    schedule.HostId,
                    schedule.CustomAnnouncementId,
                }
            join host in db.Hosts.AsNoTracking() on announcement.HostId equals host.Id
            where
                announcement.Enabled
                && host.BotRuntimeState == BotChannelRuntimeState.Started
                && (host.EnabledFeatures & HostFeatureFlags.CustomCommands)
                    == HostFeatureFlags.CustomCommands
            orderby announcement.Id
            select new AnnouncementCandidate(
                announcement,
                schedule,
                host.Login,
                host.TimeZoneId,
                host.BotRuntimeStateChangedAtUtc
            )
        ).ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var due = IsDue(candidate, now);
            if (!due.ShouldSend)
                continue;

            if (!sender.IsEnabled)
            {
                log.LogWarning(
                    "Custom announcement {AnnouncementId} is due for host {HostLogin}, but no Twitch chat sender is registered.",
                    candidate.Announcement.Id,
                    candidate.HostLogin
                );
                continue;
            }

            try
            {
                await SendAsync(db, candidate, due, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    ex,
                    "Custom announcement {AnnouncementId} failed for host {HostLogin}.",
                    candidate.Announcement.Id,
                    candidate.HostLogin
                );
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Custom announcement scheduler tick failed.");
            }

            try
            {
                await scheduler.DelayAsync(TickInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task SendAsync(
        BlokeBotDbContext db,
        AnnouncementCandidate candidate,
        AnnouncementDueResult due,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var announcement = await LoadTrackedAnnouncementAsync(db, candidate, cancellationToken);
        if (announcement is null)
            return;

        var message = messageSelector.SelectMessage(announcement.MessageLibraryEntry);
        if (string.IsNullOrWhiteSpace(message))
            return;

        await sender.SendAsync(candidate.HostLogin, message, cancellationToken);
        announcement.LastSentAtUtc = due.LastSentAtUtc ?? now;
        announcement.ChatMessagesSinceLastSent = 0;
        announcement.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<CustomAnnouncement?> LoadTrackedAnnouncementAsync(
        BlokeBotDbContext db,
        AnnouncementCandidate candidate,
        CancellationToken cancellationToken
    )
    {
        return await db
            .CustomAnnouncements.Include(x => x.MessageLibraryEntry)
            .ThenInclude(x => x!.Variants)
            .SingleOrDefaultAsync(x => x.Id == candidate.Announcement.Id, cancellationToken);
    }

    private AnnouncementDueResult IsDue(AnnouncementCandidate candidate, DateTime nowUtc) =>
        candidate.Schedule switch
        {
            IntervalCustomAnnouncementSchedule schedule => IsIntervalDue(
                candidate.Announcement,
                schedule.IntervalMinutes,
                nowUtc
            ),
            IntervalAfterChatCustomAnnouncementSchedule schedule => IsIntervalAfterChatDue(
                candidate.Announcement,
                schedule,
                nowUtc
            ),
            WeeklyCustomAnnouncementSchedule schedule => IsWeeklyDue(
                candidate,
                schedule,
                nowUtc
            ),
            _ => AnnouncementDueResult.NotDue,
        };

    private static AnnouncementDueResult IsIntervalDue(
        CustomAnnouncement announcement,
        int intervalMinutes,
        DateTime nowUtc
    )
    {
        var baseline = announcement.LastSentAtUtc ?? announcement.CreatedAtUtc;
        return baseline.AddMinutes(intervalMinutes) <= nowUtc
            ? new AnnouncementDueResult(true, nowUtc)
            : AnnouncementDueResult.NotDue;
    }

    private static AnnouncementDueResult IsIntervalAfterChatDue(
        CustomAnnouncement announcement,
        IntervalAfterChatCustomAnnouncementSchedule schedule,
        DateTime nowUtc
    )
    {
        if (announcement.ChatMessagesSinceLastSent < schedule.RequiredChatMessages)
            return AnnouncementDueResult.NotDue;

        return IsIntervalDue(announcement, schedule.IntervalMinutes, nowUtc);
    }

    private AnnouncementDueResult IsWeeklyDue(
        AnnouncementCandidate candidate,
        WeeklyCustomAnnouncementSchedule schedule,
        DateTime nowUtc
    )
    {
        var announcement = candidate.Announcement;
        var timeZone = ResolveTimeZone(candidate.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        if (localNow.DayOfWeek != schedule.Day)
            return AnnouncementDueResult.NotDue;

        var scheduledLocal = DateOnly
            .FromDateTime(localNow)
            .ToDateTime(schedule.Time, DateTimeKind.Unspecified);
        if (scheduledLocal > localNow)
            return AnnouncementDueResult.NotDue;

        if (timeZone.IsInvalidTime(scheduledLocal))
        {
            log.LogWarning(
                "Custom announcement {AnnouncementId} has an invalid scheduled local time {ScheduledLocal} in time zone {TimeZoneId}.",
                announcement.Id,
                scheduledLocal,
                candidate.TimeZoneId
            );
            return AnnouncementDueResult.NotDue;
        }

        var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, timeZone);
        if (announcement.LastSentAtUtc >= scheduledUtc)
            return AnnouncementDueResult.NotDue;

        if (
            candidate.BotRuntimeStateChangedAtUtc is { } changedAtUtc
            && changedAtUtc > scheduledUtc
        )
        {
            return AnnouncementDueResult.NotDue;
        }

        return new AnnouncementDueResult(true, scheduledUtc);
    }

    private TimeSpan TickInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(1, options.Value.CustomCommands.AnnouncementSchedulerTickSeconds)
        );

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private sealed record AnnouncementCandidate(
        CustomAnnouncement Announcement,
        CustomAnnouncementSchedule Schedule,
        string HostLogin,
        string TimeZoneId,
        DateTime? BotRuntimeStateChangedAtUtc
    );

    private sealed record AnnouncementDueResult(bool ShouldSend, DateTime? LastSentAtUtc)
    {
        public static AnnouncementDueResult NotDue { get; } = new(false, null);
    }
}
