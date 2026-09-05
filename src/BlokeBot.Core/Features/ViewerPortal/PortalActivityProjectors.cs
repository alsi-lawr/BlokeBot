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
        var board = await bingo.GetPublicAsync(channel.Host.Login, ct);
        if (board is null)
        {
            return new PortalSummaryOutcome.Disabled();
        }
        var game = board.LiveGame ?? board.Archive.FirstOrDefault();
        var link = PortalSummaryBounds.Link("Open bingo", route);
        return game is null
            ? Empty("No bingo game", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    game.TemplateName,
                    game.Status.ToString(),
                    board.LiveGame is not null,
                    [link],
                    board
                        .Archive.Prepend(board.LiveGame)
                        .OfType<BingoGameView>()
                        .SelectMany(value =>
                            value
                                .Cards.SelectMany(card => card.Wins)
                                .Select(win => new PortalActivity(
                                    win.CompletedAtUtc,
                                    $"Bingo win in {value.TemplateName}",
                                    link
                                ))
                        )
                )
            );
    }

    internal async Task<PortalSummaryOutcome> RaidAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await raid.LoadPublicAsync(channel.Host.Login, ct);
        if (board is null)
        {
            return new PortalSummaryOutcome.Disabled();
        }
        var campaign = board.ActiveCampaign ?? board.CompletedRecap;
        var link = PortalSummaryBounds.Link("Open raid", route);
        return campaign is null
            ? Empty("No raid campaign", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    campaign.BossName,
                    $"{campaign.CurrentHealth} / {campaign.MaximumHealth} health",
                    board.ActiveCampaign is not null,
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
        var result = await bounties.GetPublicBoardAsync(channel.Host.Id, ct);
        var link = PortalSummaryBounds.Link("Open bounties", route);
        return result.Match<PortalSummaryOutcome>(
            available: value =>
                value.Bounties.Count == 0
                    ? Empty("No public bounties", link)
                    : new PortalSummaryOutcome.Available(
                        PortalSummaryBounds.Create(
                            value.Bounties[0].Title,
                            value.Bounties[0].Status.ToString(),
                            value.Bounties.Any(bounty =>
                                bounty.Status
                                    is BountyStatus.Proposed
                                        or BountyStatus.Funding
                                        or BountyStatus.Accepted
                            ),
                            [link],
                            value
                                .Bounties.Where(bounty =>
                                    bounty.Status == BountyStatus.Completed
                                    && bounty.ResolvedAtUtc.HasValue
                                )
                                .Select(bounty => new PortalActivity(
                                    bounty.ResolvedAtUtc!.Value,
                                    $"Bounty completed: {bounty.Title}",
                                    link
                                ))
                        )
                    ),
            disabled: static _ => new PortalSummaryOutcome.Disabled()
        );
    }

    internal async Task<PortalSummaryOutcome> CompetitionsAsync(
        PortalChannel channel,
        string route,
        CancellationToken ct
    )
    {
        var board = await competitions.GetPublicAsync(channel.Host.Login, ct);
        if (board is null)
        {
            return new PortalSummaryOutcome.Disabled();
        }
        var competition =
            board.Active.FirstOrDefault(value =>
                value.Status is CompetitionStatus.Registration or CompetitionStatus.Running
            )
            ?? board.Active.FirstOrDefault()
            ?? board.Archive.FirstOrDefault();
        var link = PortalSummaryBounds.Link("Open tournaments", route);
        return competition is null
            ? Empty("No public tournaments", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    competition.Name,
                    competition.Status.ToString(),
                    competition.Status
                        is CompetitionStatus.Registration
                            or CompetitionStatus.Running,
                    [link],
                    board
                        .Active.Concat(board.Archive)
                        .Where(value => value.CompletedAtUtc.HasValue)
                        .Select(value => new PortalActivity(
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
        var board = await community.GetPublicAsync(channel.Host.Login, ct);
        var link = PortalSummaryBounds.Link("Open community", route);
        var season =
            board?.Seasons.FirstOrDefault(value => value.Status == CommunitySeasonStatus.Open)
            ?? board?.Seasons.FirstOrDefault();
        return season is null
            ? Empty("No public season", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    season.Name,
                    season.Status.ToString(),
                    season.Status == CommunitySeasonStatus.Open,
                    [link],
                    board!
                        .Seasons.SelectMany(value => value.Completions)
                        .Select(value => new PortalActivity(
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
        var board = await moments.GetWeeklyRecapAsync(
            channel.Host.Login,
            clock.GetUtcNow().UtcDateTime,
            ct
        );
        if (board is null)
        {
            return new PortalSummaryOutcome.Disabled();
        }
        var moment = board.Moments.OrderByDescending(value => value.ApprovedAtUtc).FirstOrDefault();
        var link = PortalSummaryBounds.Link("Open moments", route);
        return moment is null
            ? Empty("No published moments this week", link)
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    moment.PublicTitle,
                    moment.PublicCategory,
                    false,
                    [link],
                    board
                        .Moments.Where(value =>
                            value.HostId == channel.Host.Id && value.ApprovedAtUtc.HasValue
                        )
                        .Select(value => new PortalActivity(
                            value.ApprovedAtUtc!.Value,
                            value.PublicTitle,
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
