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
        var destinations = await queues.GetPublicDestinationsAsync(
            channel.Host.Id,
            PortalSummaryBounds.Items,
            ct
        );
        return destinations.Count == 0
            ? new PortalSummaryOutcome.Empty(
                PortalSummaryBounds.Create("No open queues", string.Empty, false, [])
            )
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    "Choose a queue",
                    string.Empty,
                    true,
                    destinations.Select(value =>
                        PortalSummaryBounds.Link(value.Name, route(value.Slug))
                    )
                )
            );
    }

    internal async Task<PortalSummaryOutcome> RequestsAsync(
        PortalChannel channel,
        Func<string, string> route,
        CancellationToken ct
    )
    {
        var destinations = await requests.GetPublicDestinationsAsync(
            channel.Host.Id,
            PortalSummaryBounds.Items,
            ct
        );
        return destinations.Count == 0
            ? new PortalSummaryOutcome.Empty(
                PortalSummaryBounds.Create("No open request boards", string.Empty, false, [])
            )
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    "Choose a request board",
                    string.Empty,
                    true,
                    destinations.Select(value =>
                        PortalSummaryBounds.Link(value.Title, route(value.Slug))
                    )
                )
            );
    }

    internal async Task<PortalSummaryOutcome> CollectivesAsync(
        PortalChannel channel,
        Func<CollectiveId, string> route,
        CancellationToken ct
    )
    {
        var destinations = await collectives.GetPublicDestinationsAsync(
            channel.Host.Id,
            PortalSummaryBounds.Items,
            ct
        );
        return destinations.Count == 0
            ? new PortalSummaryOutcome.Empty(
                PortalSummaryBounds.Create("No public collectives", string.Empty, false, [])
            )
            : new PortalSummaryOutcome.Available(
                PortalSummaryBounds.Create(
                    "Choose a collective",
                    string.Empty,
                    false,
                    destinations.Select(value =>
                        PortalSummaryBounds.Link(value.Name, route(value.Id))
                    )
                )
            );
    }
}
