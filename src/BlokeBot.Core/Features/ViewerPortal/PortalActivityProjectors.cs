using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.BlokeRaid;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalActivityProjectors(
    BingoService bingo,
    BlokeRaidService raid,
    BountyService bounties,
    CompetitionService competitions,
    CommunityProgressionService community,
    MomentHubService moments,
    TimeProvider clock
)
{
    internal async Task<PortalSummaryOutcome> BingoAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await bingo.GetPublicSummaryAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open bingo", route);
        return board is null ? new PortalSummaryOutcome.Disabled()
            : board.Game is not { } game ? Empty("No bingo game", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    game.Name,
                    game.Status.ToString(),
                    game.Status == BingoGameStatus.Issued,
                    [link],
                    board.Wins.Select(value => new PortalActivity(
                        value.CompletedAtUtc,
                        $"Bingo win in {value.Name}",
                        link
                    ))
                )
            );
    }

    internal async Task<PortalSummaryOutcome> RaidAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var campaign = await raid.GetPublicSummaryAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open raid", route);
        return campaign is null
            ? Empty("No raid campaign", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    campaign.BossName,
                    $"{campaign.CurrentHealth} / {campaign.MaximumHealth} health",
                    campaign.IsActive,
                    [link]
                )
            );
    }

    internal async Task<PortalSummaryOutcome> BountiesAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await bounties.GetPublicSummaryAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open bounties", route);
        return board is null ? new PortalSummaryOutcome.Disabled()
            : board.First is not { } first ? Empty("No public bounties", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    first.Title,
                    first.Status.ToString(),
                    board.IsActive,
                    [link],
                    board.Completed.Select(value => new PortalActivity(
                        value.ResolvedAtUtc!.Value,
                        $"Bounty completed: {value.Title}",
                        link
                    ))
                )
            );
    }

    internal async Task<PortalSummaryOutcome> CompetitionsAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await competitions.GetPublicSummaryAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open tournaments", route);
        return board is null ? new PortalSummaryOutcome.Disabled()
            : board.Current is not { } current ? Empty("No public tournaments", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    current.Name,
                    current.Status.ToString(),
                    current.Status is CompetitionStatus.Registration or CompetitionStatus.Running,
                    [link],
                    board.Completed.Select(value => new PortalActivity(
                        value.CompletedAtUtc!.Value,
                        $"Tournament completed: {value.Name}",
                        link
                    ))
                )
            );
    }

    internal async Task<PortalSummaryOutcome> CommunityAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await community.GetPublicSummaryAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open community", route);
        return board is null
            ? Empty("No public season", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    board.Season.Name,
                    board.Season.Status.ToString(),
                    board.Season.Status == CommunitySeasonStatus.Open,
                    [link],
                    board.Completions.Select(value => new PortalActivity(
                        value.CompletedAtUtc,
                        $"Completed: {value.DefinitionName}",
                        link
                    ))
                )
            );
    }

    internal async Task<PortalSummaryOutcome> MomentsAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await moments.GetWeeklySummaryAsync(
            channel.Host.Id,
            clock.GetUtcNow().UtcDateTime,
            ct
        );
        var link = PortalSummaryBounds.Link("Open moments", route);
        return board is null ? new PortalSummaryOutcome.Disabled()
            : board.FirstOrDefault() is not { } first
                ? Empty("No published moments this week", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    first.Title,
                    first.Category,
                    false,
                    [link],
                    board.Select(value => new PortalActivity(
                        value.ApprovedAtUtc!.Value,
                        value.Title,
                        link
                    ))
                )
            );
    }

    private static PortalSummaryOutcome Empty(string headline, PortalLink link) =>
        new PortalSummaryOutcome.Empty(
            PortalSummaryBounds.Create(headline, string.Empty, false, [link])
        );
}
