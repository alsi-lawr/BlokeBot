using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.RequestBoards;

namespace BlokeBot.Core.Features.ViewerPortal;

internal sealed class PortalDirectoryProjectors(
    PlayQueueService queues,
    RequestBoardService requests,
    CollectiveService collectives
)
{
    internal async Task<PortalSummaryOutcome> QueuesAsync(
        PortalChannel channel,
        Func<string, string> route,
        CancellationToken ct
    )
    {
        var candidates = (await queues.GetQueuesForHostAsync(channel.Host.Id, ct))
            .Where(value => value.IsOpen)
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ThenBy(value => value.Slug, StringComparer.Ordinal)
            .Take(PortalSummaryBounds.Items);
        var results = await Task.WhenAll(
            candidates.Select(candidate =>
                PortalProjectionRunner.ReadAsync(
                    async token =>
                    {
                        var page = await queues.GetPublicPageAsync(
                            channel.Host.Login,
                            candidate.Slug,
                            token
                        );
                        return
                            page is null
                            || !page.Queue.IsOpen
                            || page.Queue.HostId != channel.Host.Id
                            ? new PortalSummaryOutcome.Disabled()
                            : new PortalSummaryOutcome.Available(
                                PortalSummaryBounds.Create(
                                    page.Queue.Name,
                                    page.Queue.ActivityName,
                                    true,
                                    [
                                        PortalSummaryBounds.Link(
                                            page.Queue.Name,
                                            route(page.Queue.Slug)
                                        ),
                                    ]
                                )
                            );
                    },
                    ct
                )
            )
        );
        return Combine(results, "No open queues", "Choose a queue");
    }

    internal async Task<PortalSummaryOutcome> RequestsAsync(
        PortalChannel channel,
        Func<string, string> route,
        CancellationToken ct
    )
    {
        var candidates = (await requests.GetBoardsForHostAsync(channel.Host.Id, ct))
            .Where(value => value.IsOpen)
            .OrderBy(value => value.Title, StringComparer.Ordinal)
            .ThenBy(value => value.Slug, StringComparer.Ordinal)
            .Take(PortalSummaryBounds.Items);
        var results = await Task.WhenAll(
            candidates.Select(candidate =>
                PortalProjectionRunner.ReadAsync(
                    async token =>
                    {
                        var page = await requests.GetPublicPageAsync(
                            channel.Host.Login,
                            candidate.Slug,
                            token
                        );
                        return
                            page is null
                            || !page.Board.IsOpen
                            || page.Board.HostId != channel.Host.Id
                            ? new PortalSummaryOutcome.Disabled()
                            : new PortalSummaryOutcome.Available(
                                PortalSummaryBounds.Create(
                                    page.Board.Title,
                                    string.Empty,
                                    true,
                                    [
                                        PortalSummaryBounds.Link(
                                            page.Board.Title,
                                            route(page.Board.Slug)
                                        ),
                                    ]
                                )
                            );
                    },
                    ct
                )
            )
        );
        return Combine(results, "No open request boards", "Choose a request board");
    }

    internal async Task<PortalSummaryOutcome> CollectivesAsync(
        PortalChannel channel,
        Func<CollectiveId, string> route,
        CancellationToken ct
    )
    {
        var candidates = (await collectives.GetPublicListingsAsync(channel.Host.Id, ct)).Take(
            PortalSummaryBounds.Items
        );
        var results = await Task.WhenAll(
            candidates.Select(candidate =>
                PortalProjectionRunner.ReadAsync(
                    async token =>
                    {
                        var page = await collectives.LoadPublicAsync(
                            channel.Host.Login,
                            candidate.Id,
                            token
                        );
                        return page is null
                            ? new PortalSummaryOutcome.Disabled()
                            : new PortalSummaryOutcome.Available(
                                PortalSummaryBounds.Create(
                                    page.Name,
                                    string.Empty,
                                    false,
                                    [PortalSummaryBounds.Link(page.Name, route(page.Id))]
                                )
                            );
                    },
                    ct
                )
            )
        );
        return Combine(results, "No public collectives", "Choose a collective");
    }

    private static PortalSummaryOutcome Combine(
        IReadOnlyList<PortalSummaryOutcome> results,
        string emptyHeadline,
        string populatedHeadline
    )
    {
        var summaries = results
            .SelectMany(result =>
                result.Match<IEnumerable<PortalSummary>>(
                    available: static value => [value.Summary],
                    empty: static value => [value.Summary],
                    disabled: static _ => [],
                    degraded: static value => [value.Summary],
                    unavailable: static _ => [],
                    unauthorized: static _ => []
                )
            )
            .ToArray();
        var failed = results.Any(result =>
            result.Match(
                available: static _ => false,
                empty: static _ => false,
                disabled: static _ => false,
                degraded: static _ => true,
                unavailable: static _ => true,
                unauthorized: static _ => false
            )
        );
        if (summaries.Length == 0)
        {
            return failed
                ? new PortalSummaryOutcome.Unavailable()
                : new PortalSummaryOutcome.Empty(
                    PortalSummaryBounds.Create(emptyHeadline, string.Empty, false, [])
                );
        }
        var summary = PortalSummaryBounds.Create(
            populatedHeadline,
            string.Empty,
            summaries.Any(value => value.IsActive),
            summaries.SelectMany(value => value.Links)
        );
        return failed
            ? new PortalSummaryOutcome.Degraded(summary)
            : new PortalSummaryOutcome.Available(summary);
    }
}
