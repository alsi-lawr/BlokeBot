using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalPersonalProjectors(
    PointBalanceService points,
    GuessingHistoryService guessing,
    ViewerPassportService passports,
    ViewerPortalAccess access
)
{
    internal async Task<PortalSummaryOutcome> PointsAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await points.GetBoundedLeaderboardAsync(channel.Host.Id, publicOnly: true, ct);
        if (board is null)
        {
            return new PortalSummaryOutcome.Unavailable();
        }
        var leader = board.FirstOrDefault();
        var link = PortalSummaryBounds.Link("Open points leaderboard", route);
        return leader is null
            ? new PortalSummaryOutcome.Empty(
                PortalSummaryBounds.Create("No public points scores", string.Empty, false, [link])
            )
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(leader.Login, $"{leader.Balance} points", false, [link])
            );
    }

    internal async Task<PortalSummaryOutcome> GuessingAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var leader = await guessing.LoadPublicLeaderAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open guessing leaderboard", route);
        return leader is null
            ? new PortalSummaryOutcome.Empty(
                PortalSummaryBounds.Create("No public guessing scores", string.Empty, false, [link])
            )
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    leader.Login,
                    $"{leader.CorrectGuesses} correct guesses",
                    false,
                    [link]
                )
            );
    }

    internal async Task<PortalSummaryOutcome> PassportAsync(
        PortalChannel channel,
        PortalIdentity identity,
        string route,
        CancellationToken ct
    )
    {
        var self = await access.BindSelfAsync(channel, identity, ct);
        return await self.Match(
            anonymous: static _ => Unauthorized(),
            authenticatedSelf: value => ReadSelfAsync(value.Viewer, route, ct),
            renamed: value => ReadSelfAsync(value.Viewer, route, ct),
            erased: static _ => Unauthorized(),
            staleSession: static _ => Unauthorized(),
            unavailableAuthentication: static _ => Unauthorized()
        );
    }

    private async Task<PortalSummaryOutcome> ReadSelfAsync(
        PortalViewer viewer,
        string route,
        CancellationToken ct
    )
    {
        var result = await passports.GetSelfSummaryAsync(
            viewer.Host.Id,
            new ViewerPassportIdentity(viewer.TwitchUserId, viewer.Login, viewer.DisplayName),
            ct
        );
        return result is null ? new PortalSummaryOutcome.Unavailable()
            : result.HostId != viewer.Host.Id || result.TwitchUserId != viewer.TwitchUserId
                ? new PortalSummaryOutcome.Unauthorized()
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    "Your passport",
                    result.ProfileLine,
                    false,
                    [PortalSummaryBounds.Link("Open your passport", route)]
                )
            );
    }

    private static Task<PortalSummaryOutcome> Unauthorized() =>
        Task.FromResult<PortalSummaryOutcome>(new PortalSummaryOutcome.Unauthorized());
}
