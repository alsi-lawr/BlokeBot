using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardAutomationControls(
    DashboardAuthority authority,
    AutomationManualRunService manual,
    AutomationRunQueryService runs,
    AutomationCountdownControlService countdowns
)
{
    internal async Task<DashboardResult<AutomationManualRunOutcome>> RunAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Automation configuration,
        CancellationToken ct
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected rejected
        )
        {
            return new DashboardResult<AutomationManualRunOutcome>.Rejected(rejected.Reason);
        }
        if (
            configuration.Binding
            is not DashboardBinding.Configured { Target: DashboardTarget.Automation target }
        )
        {
            return new DashboardResult<AutomationManualRunOutcome>.Succeeded(
                new AutomationManualRunOutcome.Unavailable()
            );
        }
        var data = configuration.Data.Match<ManualSourceConfiguration?>(
            _ => null,
            replacement => new(replacement.Text)
        );
        return new DashboardResult<AutomationManualRunOutcome>.Succeeded(
            await manual.RunSourceAsync(
                new(host.Value),
                target.FlowId,
                target.SourceNodeId,
                data,
                ct
            )
        );
    }

    internal async Task<DashboardResult<AutomationRunStatusOutcome>> ReadRunsAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        ImmutableArray<AutomationRunId> returnedRunIds,
        CancellationToken ct
    ) =>
        await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected rejected
            ? new DashboardResult<AutomationRunStatusOutcome>.Rejected(rejected.Reason)
            : new DashboardResult<AutomationRunStatusOutcome>.Succeeded(
                await runs.ReadStatusAsync(new(host.Value), returnedRunIds, ct)
            );

    internal async Task<DashboardResult<AutomationCountdownControlOutcome>> ApplyCountdownAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Countdown configuration,
        CountdownOperation operation,
        TimeSpan? durationOverride,
        CancellationToken ct,
        AutomationCountdownSnapshot? selected = null
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected rejected
        )
        {
            return new DashboardResult<AutomationCountdownControlOutcome>.Rejected(rejected.Reason);
        }
        if (
            configuration.Binding
            is not DashboardBinding.Configured { Target: DashboardTarget.Countdown target }
        )
        {
            return new DashboardResult<AutomationCountdownControlOutcome>.Succeeded(
                new AutomationCountdownControlOutcome.Unavailable()
            );
        }
        if (selected is { OwnerViewerId.Length: > 0 })
        {
            return new DashboardResult<AutomationCountdownControlOutcome>.Succeeded(
                await countdowns.ApplyOwnedAsync(
                    new(host.Value),
                    selected,
                    operation,
                    durationOverride ?? configuration.DefaultDuration,
                    ct
                )
            );
        }
        var action = new CountdownActionConfiguration(
            target.Name,
            operation == CountdownOperation.Cancel
                ? TimeSpan.Zero
                : durationOverride ?? configuration.DefaultDuration,
            operation
        );
        return new DashboardResult<AutomationCountdownControlOutcome>.Succeeded(
            await countdowns.ApplyAsync(new(host.Value), action, ct)
        );
    }

    internal async Task<DashboardResult<AutomationCountdownControlOutcome>> ReadCountdownAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Countdown configuration,
        CancellationToken ct,
        AutomationCountdownSnapshot? selected = null
    ) =>
        await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected rejected
            ? new DashboardResult<AutomationCountdownControlOutcome>.Rejected(rejected.Reason)
            : new DashboardResult<AutomationCountdownControlOutcome>.Succeeded(
                configuration.Binding
                    is DashboardBinding.Configured { Target: DashboardTarget.Countdown target }
                    ? await countdowns.ReadAsync(
                        new(host.Value),
                        selected?.Name ?? target.Name,
                        ct,
                        selected?.OwnerViewerId ?? ""
                    )
                    : new AutomationCountdownControlOutcome.Unavailable()
            );

    internal async Task<
        DashboardResult<ImmutableArray<AutomationCountdownSnapshot>>
    > ReadLiveCountdownsAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        CancellationToken ct
    ) =>
        await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected rejected
            ? new DashboardResult<ImmutableArray<AutomationCountdownSnapshot>>.Rejected(
                rejected.Reason
            )
            : new DashboardResult<ImmutableArray<AutomationCountdownSnapshot>>.Succeeded(
                await countdowns.ListOwnedAsync(new(host.Value), ct)
            );
}
