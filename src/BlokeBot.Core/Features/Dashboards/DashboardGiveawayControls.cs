using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardGiveawayControls(
    DashboardAuthority authority,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointsGiveawayService giveaways
)
{
    internal Task<DashboardResult<DashboardGiveawayResult<PointsGiveawayCurrent?>>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Giveaway configuration,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            _ => giveaways.ReadCurrentAsync(host.Value, ct),
            ct
        );

    internal Task<DashboardResult<DashboardGiveawayResult<PointOperationOutcome>>> StartAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Giveaway configuration,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            login => giveaways.StartAsync(host.Value, login, null, ct),
            ct
        );

    internal Task<DashboardResult<DashboardGiveawayResult<PointOperationOutcome>>> EndAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Giveaway configuration,
        CancellationToken ct
    ) =>
        InvokeAsync(
            session,
            host,
            configuration,
            login => giveaways.EndAsync(host.Value, login, ct),
            ct
        );

    internal Task<DashboardResult<DashboardGiveawayResult<PointOperationOutcome>>> CancelAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Giveaway configuration,
        CancellationToken ct
    ) => InvokeAsync(session, host, configuration, _ => giveaways.CancelAsync(host.Value, ct), ct);

    private async Task<DashboardResult<DashboardGiveawayResult<T>>> InvokeAsync<T>(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Giveaway configuration,
        Func<string, Task<T>> operation,
        CancellationToken ct
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
        )
        {
            return new DashboardResult<DashboardGiveawayResult<T>>.Rejected(denied.Reason);
        }
        if (
            configuration.Binding
            is not DashboardBinding.Configured { Target: DashboardTarget.Giveaway }
        )
        {
            return Unavailable<T>(DashboardBindingAvailability.Unconfigured);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == host.Value)
            .Select(x => new
            {
                x.Login,
                x.EnabledFeatures,
                HasSettings = db.PointsSettings.Any(s => s.HostId == x.Id),
            })
            .SingleOrDefaultAsync(ct);
        return current is null ? Unavailable<T>(DashboardBindingAvailability.Missing)
            : !current.EnabledFeatures.Contains(HostFeatureFlags.Points)
                ? Unavailable<T>(DashboardBindingAvailability.FeatureDisabled)
            : !current.HasSettings ? Unavailable<T>(DashboardBindingAvailability.Missing)
            : new DashboardResult<DashboardGiveawayResult<T>>.Succeeded(
                new DashboardGiveawayResult<T>.Ready(await operation(current.Login))
            );
    }

    private static DashboardResult<DashboardGiveawayResult<T>> Unavailable<T>(
        DashboardBindingAvailability reason
    ) =>
        new DashboardResult<DashboardGiveawayResult<T>>.Succeeded(
            new DashboardGiveawayResult<T>.Unavailable(reason)
        );
}
