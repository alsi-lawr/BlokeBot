using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationCueLifecycleObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    Func<AutomationRuntimeService> runtime,
    TimeProvider clock
) : IOverlayCueLifecycleObserver
{
    public async Task CueChangedAsync(
        OverlayCueLifecycleNotice notice,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == notice.HostId, cancellationToken);
        if (
            host is null
            || !host.EnabledFeatures.Contains(
                BlokeBot.Persistence.Models.HostFeatureFlags.Automations
                    | BlokeBot.Persistence.Models.HostFeatureFlags.Overlays
            )
        )
        {
            return;
        }
        var kind = notice.Kind switch
        {
            OverlayCueLifecycleKind.Queued => CueLifecycleKind.Queued,
            OverlayCueLifecycleKind.Started => CueLifecycleKind.Started,
            OverlayCueLifecycleKind.Finished => CueLifecycleKind.Finished,
            OverlayCueLifecycleKind.Interrupted => CueLifecycleKind.Interrupted,
        };
        _ = await runtime()
            .DispatchExpandedAsync(
                Create(
                    host,
                    AutomationDefinitionIds.CueLifecycleSource,
                    $"{notice.RunId}:{kind}",
                    notice.AtUtc,
                    clock.GetUtcNow(),
                    [
                        Text("event-kind", kind.ToString()),
                        Text("cue-id", notice.CueId.ToString()),
                        Text("cue-run-id", notice.RunId.ToString()),
                        Text("target-id", notice.TargetId.ToString()),
                        Text("playback-outcome", OutcomeLabel(notice.Outcome)),
                    ]
                ),
                c =>
                    c is CueLifecycleSourceConfiguration s
                    && s.Event == kind
                    && (
                        s.CueId is null
                        || (Guid.TryParse(s.CueId, out var cue) && cue == notice.CueId)
                    )
                    && (
                        s.TargetId is null
                        || (Guid.TryParse(s.TargetId, out var target) && target == notice.TargetId)
                    ),
                cancellationToken,
                deferExecution: true
            );
    }

    private static string OutcomeLabel(OverlayCueLifecycleOutcome outcome) =>
        outcome switch
        {
            OverlayCueLifecycleOutcome.Queued => "queued",
            OverlayCueLifecycleOutcome.QueuedDisconnected => "queued-disconnected",
            OverlayCueLifecycleOutcome.ServerStartedUnconfirmed => "server-started-unconfirmed",
            OverlayCueLifecycleOutcome.TimeDerivedEndUnconfirmed => "time-derived-end-unconfirmed",
            OverlayCueLifecycleOutcome.BrowserReportedEndUnverified =>
                "browser-reported-end-unverified",
            OverlayCueLifecycleOutcome.QueueExpiredUnavailable => "queue-expired-unavailable",
            OverlayCueLifecycleOutcome.CancelledOrTargetUnavailable =>
                "cancelled-or-target-unavailable",
            OverlayCueLifecycleOutcome.CancelledWhileQueued => "cancelled-while-queued",
        };
}
