using System.Diagnostics;
using BlokeBot.Announcements;
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
        var now = scheduler.GetUtcNow();
        await using var candidateDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await (
            from announcement in candidateDb.CustomAnnouncements.AsNoTracking()
            join host in candidateDb.Hosts.AsNoTracking() on announcement.HostId equals host.Id
            where
                announcement.Enabled
                && host.BotRuntimeState == BotChannelRuntimeState.Started
                && (host.EnabledFeatures & HostFeatureFlags.CustomCommands)
                    == HostFeatureFlags.CustomCommands
            orderby announcement.Id
            select new AnnouncementCandidate(
                announcement.Id,
                host.Login,
                host.TimeZoneId,
                host.BotRuntimeStateChangedAtUtc
            )
        ).ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            try
            {
                await ProcessCandidateAsync(candidate, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                log.LogError(
                    "Custom announcement {AnnouncementId} candidate processing failed for host {HostLogin}; FailureType: {FailureType}.",
                    candidate.AnnouncementId,
                    candidate.HostLogin,
                    exception.GetType().Name
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
            catch (Exception exception)
            {
                log.LogError(
                    "Custom announcement scheduler tick failed; FailureType: {FailureType}.",
                    exception.GetType().Name
                );
            }

            try
            {
                await scheduler.DelayAsync(TickInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task ProcessCandidateAsync(
        AnnouncementCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var announcement = await db
            .CustomAnnouncements.Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Include(x => x.MessageLibraryEntry)
                .ThenInclude(x => x!.Variants)
            .SingleOrDefaultAsync(x => x.Id == candidate.AnnouncementId, cancellationToken);
        if (announcement is null || !announcement.Enabled)
        {
            return;
        }

        var policy = AnnouncementDeliveryPolicyMapper.ToDomain(announcement.DeliveryPolicy);
        await policy.Match(retry =>
            ProcessRetryUntilExpiredThenSkipAsync(
                db,
                announcement,
                candidate,
                retry,
                now,
                cancellationToken
            )
        );
    }

    private async Task ProcessRetryUntilExpiredThenSkipAsync(
        BlokeBotDbContext db,
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        AnnouncementDeliveryPolicy.RetryUntilExpiredThenSkip policy,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.Attempting)
        {
            CompleteOccurrence(announcement, AnnouncementOccurrenceStatus.TerminalAmbiguous, now);
            await db.SaveChangesAsync(cancellationToken);
            LogTerminal(announcement, candidate, "InterruptedAttempt");
            return;
        }

        var schedule = EvaluateSchedule(announcement, candidate, now);
        if (schedule is AnnouncementScheduleEvaluation.InvalidTimeZone)
        {
            if (
                announcement.OccurrenceStatus
                != AnnouncementOccurrenceStatus.TerminalInvalidTimeZone
            )
            {
                MarkInvalidTimeZone(announcement, now);
                await db.SaveChangesAsync(cancellationToken);
                LogTerminal(announcement, candidate, "InvalidTimeZone");
            }

            return;
        }

        if (announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.TerminalInvalidTimeZone)
        {
            ResetOccurrence(announcement);
            await db.SaveChangesAsync(cancellationToken);
        }

        var due = ((AnnouncementScheduleEvaluation.Evaluated)schedule).Due;
        if (due is AnnouncementDueResult.NotDue)
        {
            return;
        }

        var dueAt = ((AnnouncementDueResult.Due)due).DueAt;
        if (announcement.OccurrenceDueAtUtc != dueAt.UtcDateTime)
        {
            StartOccurrence(announcement, dueAt, policy.OccurrenceLifetime);
            await db.SaveChangesAsync(cancellationToken);
        }

        var expiresAt = RequireUtc(announcement.OccurrenceExpiresAtUtc, "expiry");
        if (now >= expiresAt)
        {
            CompleteOccurrence(announcement, AnnouncementOccurrenceStatus.SkippedExpired, now);
            await db.SaveChangesAsync(cancellationToken);
            LogTerminal(announcement, candidate, "Expired");
            return;
        }

        if (
            announcement.OccurrenceStatus == AnnouncementOccurrenceStatus.RetryScheduled
            && now < RequireUtc(announcement.OccurrenceNextAttemptAtUtc, "next attempt")
        )
        {
            return;
        }

        if (
            announcement.OccurrenceStatus
            is not (
                AnnouncementOccurrenceStatus.Pending
                or AnnouncementOccurrenceStatus.RetryScheduled
            )
        )
        {
            return;
        }

        var message = announcement.OccurrenceStatus switch
        {
            AnnouncementOccurrenceStatus.Pending => messageSelector.SelectMessage(
                announcement.MessageLibraryEntry
            ),
            AnnouncementOccurrenceStatus.RetryScheduled => announcement.OccurrenceMessage
                ?? throw new UnreachableException(
                    "A retry-scheduled announcement occurrence requires its selected message."
                ),
            _ => throw new UnreachableException(
                "Only pending or retry-scheduled occurrences can enqueue."
            ),
        };
        if (string.IsNullOrWhiteSpace(message))
        {
            CompleteOccurrence(
                announcement,
                AnnouncementOccurrenceStatus.TerminalMissingMessage,
                now
            );
            await db.SaveChangesAsync(cancellationToken);
            LogTerminal(announcement, candidate, "MissingMessage");
            return;
        }

        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.Attempting;
        announcement.OccurrenceAttemptCount = checked(announcement.OccurrenceAttemptCount + 1);
        announcement.OccurrenceNextAttemptAtUtc = null;
        announcement.OccurrenceMessage = message;
        announcement.UpdatedAtUtc = now.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        var outcome = await sender.EnqueueAsync(
            candidate.HostLogin,
            message,
            expiresAt,
            cancellationToken
        );
        string? terminalReason = null;
        switch (outcome)
        {
            case AnnouncementEnqueueOutcome.Accepted:
                CompleteOccurrence(announcement, AnnouncementOccurrenceStatus.Accepted, now);
                announcement.LastSentAtUtc = dueAt.UtcDateTime;
                announcement.ChatMessagesSinceLastSent = 0;
                break;
            case AnnouncementEnqueueOutcome.SafePreEnqueueTransient transient:
                ScheduleRetry(announcement, policy.RetryDelay, now, expiresAt);
                log.LogWarning(
                    "Custom announcement {AnnouncementId} scheduled safe pre-enqueue retry for host {HostLogin}; Attempt: {Attempt}; FailureType: {FailureType}; NextAttemptAtUtc: {NextAttemptAtUtc}.",
                    announcement.Id,
                    candidate.HostLogin,
                    announcement.OccurrenceAttemptCount,
                    transient.FailureType.Value,
                    announcement.OccurrenceNextAttemptAtUtc
                );
                break;
            case AnnouncementEnqueueOutcome.Rejected:
                CompleteOccurrence(
                    announcement,
                    AnnouncementOccurrenceStatus.TerminalRejected,
                    now
                );
                terminalReason = "Rejected";
                break;
            case AnnouncementEnqueueOutcome.Ambiguous ambiguous:
                CompleteOccurrence(
                    announcement,
                    AnnouncementOccurrenceStatus.TerminalAmbiguous,
                    now
                );
                terminalReason = ambiguous.FailureType.Value;
                break;
            case AnnouncementEnqueueOutcome.Unexpected unexpected:
                CompleteOccurrence(
                    announcement,
                    AnnouncementOccurrenceStatus.TerminalUnexpected,
                    now
                );
                terminalReason = unexpected.FailureType.Value;
                break;
            default:
                throw new UnreachableException("Unknown announcement enqueue outcome.");
        }

        await db.SaveChangesAsync(cancellationToken);
        if (IsTerminal(announcement.OccurrenceStatus))
        {
            LogTerminal(
                announcement,
                candidate,
                terminalReason ?? announcement.OccurrenceStatus.ToString()
            );
        }
    }

    private static AnnouncementScheduleEvaluation EvaluateSchedule(
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        DateTimeOffset now
    )
    {
        return announcement.Schedule switch
        {
            IntervalCustomAnnouncementSchedule interval =>
                new AnnouncementScheduleEvaluation.Evaluated(
                    IsIntervalDue(announcement, interval.IntervalMinutes, now)
                ),
            IntervalAfterChatCustomAnnouncementSchedule intervalAfterChat =>
                new AnnouncementScheduleEvaluation.Evaluated(
                    announcement.ChatMessagesSinceLastSent < intervalAfterChat.RequiredChatMessages
                        ? new AnnouncementDueResult.NotDue()
                        : IsIntervalDue(announcement, intervalAfterChat.IntervalMinutes, now)
                ),
            WeeklyCustomAnnouncementSchedule weekly => EvaluateWeeklySchedule(
                announcement,
                candidate,
                weekly,
                now
            ),
            _ => throw new UnreachableException("Unknown custom announcement schedule."),
        };
    }

    private static AnnouncementDueResult IsIntervalDue(
        CustomAnnouncement announcement,
        int intervalMinutes,
        DateTimeOffset now
    )
    {
        var baseline = AsUtc(
            announcement.LastOccurrenceAtUtc
                ?? announcement.LastSentAtUtc
                ?? announcement.CreatedAtUtc
        );
        var dueAt = baseline.AddMinutes(intervalMinutes);
        return dueAt <= now
            ? new AnnouncementDueResult.Due(dueAt)
            : new AnnouncementDueResult.NotDue();
    }

    private static AnnouncementScheduleEvaluation EvaluateWeeklySchedule(
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        WeeklyCustomAnnouncementSchedule schedule,
        DateTimeOffset now
    )
    {
        var timeZone = ResolveTimeZone(candidate.TimeZoneId);
        if (timeZone is null)
        {
            return new AnnouncementScheduleEvaluation.InvalidTimeZone();
        }

        var utcNow = now.UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        if (localNow.DayOfWeek != schedule.Day)
        {
            return new AnnouncementScheduleEvaluation.Evaluated(new AnnouncementDueResult.NotDue());
        }

        var scheduledLocal = DateOnly
            .FromDateTime(localNow)
            .ToDateTime(schedule.Time, DateTimeKind.Unspecified);
        if (scheduledLocal > localNow)
        {
            return new AnnouncementScheduleEvaluation.Evaluated(new AnnouncementDueResult.NotDue());
        }

        if (timeZone.IsInvalidTime(scheduledLocal))
        {
            return new AnnouncementScheduleEvaluation.InvalidTimeZone();
        }

        var dueAt = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(scheduledLocal, timeZone),
            TimeSpan.Zero
        );
        if ((announcement.LastOccurrenceAtUtc ?? announcement.LastSentAtUtc) >= dueAt.UtcDateTime)
        {
            return new AnnouncementScheduleEvaluation.Evaluated(new AnnouncementDueResult.NotDue());
        }

        if (
            candidate.BotRuntimeStateChangedAtUtc is { } changedAtUtc
            && changedAtUtc > dueAt.UtcDateTime
        )
        {
            return new AnnouncementScheduleEvaluation.Evaluated(new AnnouncementDueResult.NotDue());
        }

        return new AnnouncementScheduleEvaluation.Evaluated(new AnnouncementDueResult.Due(dueAt));
    }

    private static TimeZoneInfo? ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static void StartOccurrence(
        CustomAnnouncement announcement,
        DateTimeOffset dueAt,
        AnnouncementOccurrenceLifetime lifetime
    )
    {
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.Pending;
        announcement.OccurrenceDueAtUtc = dueAt.UtcDateTime;
        announcement.OccurrenceExpiresAtUtc = dueAt.Add(lifetime.Value).UtcDateTime;
        announcement.OccurrenceNextAttemptAtUtc = dueAt.UtcDateTime;
        announcement.OccurrenceCompletedAtUtc = null;
        announcement.OccurrenceAttemptCount = 0;
        announcement.OccurrenceMessage = null;
    }

    private static void ScheduleRetry(
        CustomAnnouncement announcement,
        AnnouncementRetryDelay retryDelay,
        DateTimeOffset now,
        DateTimeOffset expiresAt
    )
    {
        var nextAttempt = now.Add(retryDelay.Value);
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.RetryScheduled;
        announcement.OccurrenceNextAttemptAtUtc = Min(nextAttempt, expiresAt).UtcDateTime;
        announcement.UpdatedAtUtc = now.UtcDateTime;
    }

    private static void CompleteOccurrence(
        CustomAnnouncement announcement,
        AnnouncementOccurrenceStatus status,
        DateTimeOffset completedAt
    )
    {
        if (announcement.OccurrenceDueAtUtc is { } dueAt)
        {
            announcement.LastOccurrenceAtUtc = dueAt;
        }

        announcement.OccurrenceStatus = status;
        announcement.OccurrenceNextAttemptAtUtc = null;
        announcement.OccurrenceCompletedAtUtc = completedAt.UtcDateTime;
        announcement.OccurrenceMessage = null;
        announcement.UpdatedAtUtc = completedAt.UtcDateTime;
    }

    private static void MarkInvalidTimeZone(
        CustomAnnouncement announcement,
        DateTimeOffset completedAt
    )
    {
        ResetOccurrence(announcement);
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.TerminalInvalidTimeZone;
        announcement.OccurrenceCompletedAtUtc = completedAt.UtcDateTime;
        announcement.UpdatedAtUtc = completedAt.UtcDateTime;
    }

    private static void ResetOccurrence(CustomAnnouncement announcement)
    {
        announcement.OccurrenceStatus = AnnouncementOccurrenceStatus.None;
        announcement.OccurrenceDueAtUtc = null;
        announcement.OccurrenceExpiresAtUtc = null;
        announcement.OccurrenceNextAttemptAtUtc = null;
        announcement.OccurrenceCompletedAtUtc = null;
        announcement.OccurrenceAttemptCount = 0;
        announcement.OccurrenceMessage = null;
    }

    private void LogTerminal(
        CustomAnnouncement announcement,
        AnnouncementCandidate candidate,
        string reason
    )
    {
        log.LogWarning(
            "Custom announcement {AnnouncementId} occurrence completed for host {HostLogin}; Status: {Status}; Reason: {Reason}; AttemptCount: {AttemptCount}; DueAtUtc: {DueAtUtc}; ExpiresAtUtc: {ExpiresAtUtc}.",
            announcement.Id,
            candidate.HostLogin,
            announcement.OccurrenceStatus,
            reason,
            announcement.OccurrenceAttemptCount,
            announcement.OccurrenceDueAtUtc,
            announcement.OccurrenceExpiresAtUtc
        );
    }

    private static bool IsTerminal(AnnouncementOccurrenceStatus status)
    {
        return status
            is AnnouncementOccurrenceStatus.SkippedExpired
                or AnnouncementOccurrenceStatus.TerminalRejected
                or AnnouncementOccurrenceStatus.TerminalAmbiguous
                or AnnouncementOccurrenceStatus.TerminalUnexpected
                or AnnouncementOccurrenceStatus.TerminalInvalidTimeZone
                or AnnouncementOccurrenceStatus.TerminalMissingMessage;
    }

    private TimeSpan TickInterval()
    {
        return TimeSpan.FromSeconds(
            Math.Max(1, options.Value.CustomCommands.AnnouncementSchedulerTickSeconds)
        );
    }

    private static DateTimeOffset RequireUtc(DateTime? value, string field)
    {
        return value is { } dateTime
            ? AsUtc(dateTime)
            : throw new UnreachableException($"Announcement occurrence {field} is required.");
    }

    private static DateTimeOffset AsUtc(DateTime value)
    {
        return new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }

    private sealed record AnnouncementCandidate(
        int AnnouncementId,
        string HostLogin,
        string? TimeZoneId,
        DateTime? BotRuntimeStateChangedAtUtc
    );

    private abstract record AnnouncementScheduleEvaluation
    {
        private AnnouncementScheduleEvaluation() { }

        internal sealed record Evaluated(AnnouncementDueResult Due)
            : AnnouncementScheduleEvaluation;

        internal sealed record InvalidTimeZone : AnnouncementScheduleEvaluation;
    }

    private abstract record AnnouncementDueResult
    {
        private AnnouncementDueResult() { }

        internal sealed record Due(DateTimeOffset DueAt) : AnnouncementDueResult;

        internal sealed record NotDue : AnnouncementDueResult;
    }
}
