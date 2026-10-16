using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class ExpandedAutomationRuntime
{
    private async Task TickSchedulesAsync(
        BotHost host,
        bool connected,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var sources = await runtime.ConfiguredSchedulesAsync(new(host.Id), ct);
        var groups = sources
            .Where(source => source.Configuration is ScheduledTimeSourceConfiguration)
            .GroupBy(source => (ScheduledTimeSourceConfiguration)source.Configuration!)
            .ToArray();
        foreach (var group in groups)
        {
            var schedule = group.Key;
            var key = (host.Id, schedule.ToString());
            if (!_nextSchedules.TryGetValue(key, out var next))
            {
                if (AutomationScheduleTime.Next(schedule, now) is { } first)
                {
                    _nextSchedules[key] = first;
                }
                continue;
            }
            if (next > now)
            {
                continue;
            }
            _nextSchedules[key] =
                AutomationScheduleTime.Next(schedule, now) ?? DateTimeOffset.MaxValue;
            var enabled = ImmutableArray.CreateBuilder<AutomationConfiguredSource>();
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                foreach (var source in group)
                {
                    var reason =
                        source.Eligibility is AutomationSourceEligibility.Disabled
                            ? AutomationScheduleSkipReason.Disabled
                        : !host.EnabledFeatures.Contains(HostFeatureFlags.Automations)
                            ? AutomationScheduleSkipReason.FeatureDisabled
                        : source.Eligibility.Match<AutomationScheduleSkipReason?>(
                            _ => null,
                            _ => AutomationScheduleSkipReason.Disabled,
                            _ => AutomationScheduleSkipReason.NotReady
                        );
                    if (reason is { } skipped)
                    {
                        await RecordAsync(db, source, skipped, next, now, ct);
                    }
                    else
                    {
                        enabled.Add(source);
                    }
                }
                if (enabled.Count == 0)
                {
                    continue;
                }
                AutomationScheduleSkipReason? gate =
                    !connected ? AutomationScheduleSkipReason.NotReady
                    : now - next > TimeSpan.FromSeconds(2) ? AutomationScheduleSkipReason.Late
                    : null;
                if (gate is null && schedule.LiveOnly)
                {
                    var live = await LiveAsync(host, ct);
                    if (live is not HostStreamLivenessOutcome.Live)
                    {
                        gate =
                            live is HostStreamLivenessOutcome.Offline
                                ? AutomationScheduleSkipReason.Offline
                                : AutomationScheduleSkipReason.StreamStatusUnknown;
                    }
                }
                if (gate is { } blocked)
                {
                    foreach (var source in enabled)
                    {
                        await RecordAsync(db, source, blocked, next, now, ct);
                    }
                    continue;
                }
            }
            var context = Create(
                host,
                AutomationDefinitionIds.ScheduledTimeSource,
                $"{schedule}:{next.UtcTicks}",
                next,
                now,
                [Time("scheduled-at", next), Text("schedule-zone", schedule.Zone)]
            );
            await runtime.DispatchObservedAsync(
                new(host.Id),
                (_, _) =>
                    Task.FromResult<ImmutableArray<AutomationObservedTrigger>>([
                        new(context, c => c == schedule),
                    ]),
                ct,
                async (db, _, outcome, token) =>
                {
                    foreach (var rejection in outcome.Rejections)
                    {
                        foreach (var node in rejection.SourceNodeIds)
                        {
                            await AutomationScheduleSkipStore.AppendAsync(
                                db,
                                new(host.Id),
                                rejection.FlowId,
                                node,
                                SkipReason(rejection.Reason),
                                next,
                                now,
                                token
                            );
                        }
                    }
                    AutomationScheduleSkipReason? reason = outcome.Status switch
                    {
                        AutomationDispatchStatus.NoMatchingFlow =>
                            AutomationScheduleSkipReason.NoCurrentMatch,
                        AutomationDispatchStatus.FeatureDisabled =>
                            AutomationScheduleSkipReason.FeatureDisabled,
                        AutomationDispatchStatus.HostNotFound =>
                            AutomationScheduleSkipReason.NotReady,
                        _ => null,
                    };
                    if (reason is { } skipped && outcome.Rejections.IsEmpty)
                    {
                        foreach (var source in enabled)
                        {
                            await RecordAsync(db, source, skipped, next, now, token);
                        }
                    }
                }
            );
        }
        foreach (
            var key in _nextSchedules.Keys.Where(key =>
                key.Host == host.Id && !groups.Any(group => group.Key.ToString() == key.Key)
            )
        )
        {
            _ = _nextSchedules.TryRemove(key, out _);
        }

        Task RecordAsync(
            BlokeBotDbContext db,
            AutomationConfiguredSource source,
            AutomationScheduleSkipReason reason,
            DateTimeOffset scheduled,
            DateTimeOffset observed,
            CancellationToken token
        ) =>
            AutomationScheduleSkipStore.AppendAsync(
                db,
                new(host.Id),
                source.FlowId,
                source.NodeId,
                reason,
                scheduled,
                observed,
                token
            );
    }

    private static AutomationScheduleSkipReason SkipReason(
        AutomationAdmissionRejectionReason reason
    ) =>
        reason switch
        {
            AutomationAdmissionRejectionReason.SourceReconstruction =>
                AutomationScheduleSkipReason.SourceReconstruction,
            AutomationAdmissionRejectionReason.SourceConfiguration =>
                AutomationScheduleSkipReason.SourceConfiguration,
            AutomationAdmissionRejectionReason.SubflowClosure =>
                AutomationScheduleSkipReason.SubflowClosure,
            AutomationAdmissionRejectionReason.GraphValidation =>
                AutomationScheduleSkipReason.GraphValidation,
            AutomationAdmissionRejectionReason.ExecutionUnavailable =>
                AutomationScheduleSkipReason.ExecutionUnavailable,
            AutomationAdmissionRejectionReason.SubflowReferences =>
                AutomationScheduleSkipReason.SubflowReferences,
            AutomationAdmissionRejectionReason.FeatureDisabled =>
                AutomationScheduleSkipReason.FeatureDisabled,
            AutomationAdmissionRejectionReason.FlowUnfinishedLimit =>
                AutomationScheduleSkipReason.FlowUnfinishedLimit,
            AutomationAdmissionRejectionReason.FlowWaitingLimit =>
                AutomationScheduleSkipReason.FlowWaitingLimit,
            AutomationAdmissionRejectionReason.ChannelUnfinishedLimit =>
                AutomationScheduleSkipReason.ChannelUnfinishedLimit,
            AutomationAdmissionRejectionReason.ChannelWaitingLimit =>
                AutomationScheduleSkipReason.ChannelWaitingLimit,
        };
}
