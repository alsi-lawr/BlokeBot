using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPassports;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalPersonalProjectors(
    PointBalanceService points,
    GuessingHistoryService guessing,
    ViewerPassportPublicIdentityPolicy privacy,
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
        var exclusions = await privacy.ExclusionsAsync(channel.Host.Id, ct);
        var board = await points.GetPublicLeaderboardAsync(
            channel.Host.Id,
            PortalSummaryBounds.Items,
            exclusions.Logins,
            ct
        );
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
        var exclusions = await privacy.ExclusionsAsync(channel.Host.Id, ct);
        var board = await guessing.LoadPublicLeaderboardAsync(
            channel.Host.Id,
            new GuessHistoryQuery { Page = 1, PageSize = 10 },
            exclusions.Logins,
            ct
        );
        var leader = board.Entries.FirstOrDefault();
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
        var result = await passports.GetSelfAsync(
            viewer.Host.Id,
            new ViewerPassportIdentity(viewer.TwitchUserId, viewer.Login, viewer.DisplayName),
            ct
        );
        return result switch
        {
            ViewerPassportQueryOutcome.Available available
                when available.Passport.HostId == viewer.Host.Id
                    && available.Passport.TwitchUserId == viewer.TwitchUserId =>
                new PortalSummaryOutcome.Available(
                    PortalSummaryBounds.Create(
                        "Your passport",
                        available.Passport.ProfileLine,
                        false,
                        [PortalSummaryBounds.Link("Open your passport", route)]
                    )
                ),
            ViewerPassportQueryOutcome.Available => new PortalSummaryOutcome.Unauthorized(),
            ViewerPassportQueryOutcome.FeatureDisabled => new PortalSummaryOutcome.Disabled(),
            ViewerPassportQueryOutcome.Forbidden => new PortalSummaryOutcome.Unauthorized(),
            ViewerPassportQueryOutcome.NotFound => new PortalSummaryOutcome.Unauthorized(),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static Task<PortalSummaryOutcome> Unauthorized() =>
        Task.FromResult<PortalSummaryOutcome>(new PortalSummaryOutcome.Unauthorized());
}
