using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardPollControls(
    DashboardAuthority authority,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IPollDashboardOperations polls
)
{
    internal async Task<DashboardResult<PollControlState>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Poll configuration,
        CancellationToken ct
    ) =>
        await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
            ? new DashboardResult<PollControlState>.Rejected(denied.Reason)
        : configuration.Binding
            is DashboardBinding.Configured { Target: DashboardTarget.Poll target }
            ? new DashboardResult<PollControlState>.Succeeded(
                await polls.ReadControlAsync(host.Value, target.TemplateId, ct)
            )
        : new DashboardResult<PollControlState>.Rejected(DashboardRejection.Invalid);

    internal Task<DashboardResult<PollOperationOutcome>> StartAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Poll configuration,
        CancellationToken ct
    ) => InvokeAsync(session, host, configuration, id => polls.StartAsync(host.Value, id, ct), ct);

    internal Task<DashboardResult<PollOperationOutcome>> EndAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Poll configuration,
        bool confirmedExternal,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            _ => polls.EndAsync(host.Value, confirmedExternal, ct),
            ct
        );

    private async Task<DashboardResult<PollOperationOutcome>> InvokeAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Poll configuration,
        Func<int, Task<PollOperationOutcome>> operation,
        CancellationToken ct
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
        )
        {
            return new DashboardResult<PollOperationOutcome>.Rejected(denied.Reason);
        }
        if (
            configuration.Binding
            is not DashboardBinding.Configured { Target: DashboardTarget.Poll target }
        )
        {
            return new DashboardResult<PollOperationOutcome>.Succeeded(
                new PollOperationOutcome.TemplateNotFound()
            );
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new DashboardResult<PollOperationOutcome>.Succeeded(
            await db.TwitchPollTemplates.AnyAsync(
                x => x.HostId == host.Value && x.Id == target.TemplateId,
                ct
            )
                ? await operation(target.TemplateId)
                : new PollOperationOutcome.TemplateNotFound()
        );
    }
}
