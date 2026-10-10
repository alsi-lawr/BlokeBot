using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardGuessingControls(
    DashboardAuthority authority,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingDashboardService queries,
    GuessingRoundService rounds
)
{
    internal Task<DashboardResult<DashboardGuessingResult<GuessingControlState>>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Guessing configuration,
        CancellationToken ct
    ) =>
        InvokeAsync<GuessingControlState>(
            session,
            host,
            configuration,
            async profileId =>
                await queries.ReadControlAsync(host.Value, profileId, ct) is { } state
                    ? new DashboardGuessingResult<GuessingControlState>.Ready(state)
                    : new DashboardGuessingResult<GuessingControlState>.Unavailable(
                        DashboardBindingAvailability.Missing
                    ),
            ct
        );

    internal Task<DashboardResult<DashboardGuessingResult<GuessingOperationOutcome>>> StartAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Guessing configuration,
        CancellationToken ct
    ) =>
        InvokeAsync<GuessingOperationOutcome>(
            session,
            host,
            configuration,
            async profileId => new DashboardGuessingResult<GuessingOperationOutcome>.Ready(
                await rounds.StartRound(host.Value, profileId).RunAsync(ct)
            ),
            ct
        );

    internal Task<DashboardResult<DashboardGuessingResult<GuessingOperationOutcome>>> StopAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Guessing configuration,
        CancellationToken ct
    ) =>
        InvokeAsync<GuessingOperationOutcome>(
            session,
            host,
            configuration,
            async _ => new DashboardGuessingResult<GuessingOperationOutcome>.Ready(
                await rounds.StopGuessing(host.Value).RunAsync(ct)
            ),
            ct
        );

    internal Task<
        DashboardResult<DashboardGuessingResult<GuessingWinnerDeclarationOutcome>>
    > DeclareWinnerAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Guessing configuration,
        GuessName winner,
        CancellationToken ct
    ) =>
        InvokeAsync<GuessingWinnerDeclarationOutcome>(
            session,
            host,
            configuration,
            async _ => new DashboardGuessingResult<GuessingWinnerDeclarationOutcome>.Ready(
                await rounds.DeclareWinner(host.Value, winner.Value).RunAsync(ct)
            ),
            ct
        );

    private async Task<DashboardResult<DashboardGuessingResult<T>>> InvokeAsync<T>(
        AuthenticatedSession session,
        DashboardHostId host,
        DashboardWidgetConfiguration.Guessing configuration,
        Func<int, Task<DashboardGuessingResult<T>>> operation,
        CancellationToken ct
    )
    {
        if (
            await authority.AuthorizeAsync(session, host, ct)
            is DashboardResult<DashboardHostId>.Rejected denied
        )
        {
            return new DashboardResult<DashboardGuessingResult<T>>.Rejected(denied.Reason);
        }
        if (
            configuration.Binding
            is not DashboardBinding.Configured { Target: DashboardTarget.Guessing target }
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
                x.EnabledFeatures,
                HasProfile = db.Profiles.Any(p => p.HostId == x.Id && p.Id == target.ProfileId),
            })
            .SingleOrDefaultAsync(ct);
        return current is null ? Unavailable<T>(DashboardBindingAvailability.Missing)
            : !current.EnabledFeatures.Contains(HostFeatureFlags.Guessing)
                ? Unavailable<T>(DashboardBindingAvailability.FeatureDisabled)
            : !current.HasProfile ? Unavailable<T>(DashboardBindingAvailability.Missing)
            : new DashboardResult<DashboardGuessingResult<T>>.Succeeded(
                await operation(target.ProfileId)
            );
    }

    private static DashboardResult<DashboardGuessingResult<T>> Unavailable<T>(
        DashboardBindingAvailability reason
    ) =>
        new DashboardResult<DashboardGuessingResult<T>>.Succeeded(
            new DashboardGuessingResult<T>.Unavailable(reason)
        );
}
