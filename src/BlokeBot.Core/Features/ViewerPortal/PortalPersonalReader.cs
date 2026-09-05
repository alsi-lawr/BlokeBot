using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

internal enum PortalSelfOwner
{
    Passport,
    Queue,
    Requests,
    Bingo,
}

internal enum PortalReadState
{
    Ready,
    Unavailable,
}

internal sealed record PortalPersonalItem(
    string Label,
    string Value,
    string Detail,
    PortalLink Link,
    int? Rank = null
);

internal sealed record PortalPersonalProjection(
    PortalReadState State,
    ImmutableArray<PortalPersonalItem> Items
);

internal sealed class PortalPersonalReader(
    ViewerPortalAccess access,
    ViewerPassportService passports,
    PlayQueueService queues,
    RequestBoardService requests,
    BingoService bingo,
    PortalReadScheduler scheduler,
    PortalReadTelemetry telemetry
)
{
    internal async Task<PortalPersonalProjection> ReadAsync(
        PortalChannel channel,
        AuthenticatedSession session,
        PortalSelfOwner owner,
        CancellationToken ct
    )
    {
        var icon = owner switch
        {
            PortalSelfOwner.Passport => PortalIcon.Passport,
            PortalSelfOwner.Queue => PortalIcon.Queue,
            PortalSelfOwner.Requests => PortalIcon.Request,
            PortalSelfOwner.Bingo => PortalIcon.Bingo,
        };
        var started = Stopwatch.GetTimestamp();
        using var activity = telemetry.Start(icon, PortalAudience.Self);
        var outcome = PortalReadOutcome.Unavailable;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var items = await scheduler
                .ReadAsync(token => ReadBoundAsync(channel, session, owner, token), timeout.Token)
                .WaitAsync(timeout.Token);
            ct.ThrowIfCancellationRequested();
            if (
                JsonSerializer.SerializeToUtf8Bytes(items).Length
                > PortalProjectionRunner.MaximumSummaryBytes
            )
            {
                outcome = PortalReadOutcome.BudgetExceeded;
                return new(PortalReadState.Unavailable, []);
            }
            outcome = PortalReadOutcome.Available;
            return new(PortalReadState.Ready, items);
        }
        catch (Exception)
        {
            outcome = ct.IsCancellationRequested
                ? PortalReadOutcome.Cancelled
                : PortalReadOutcome.Unavailable;
            ct.ThrowIfCancellationRequested();
            return new(PortalReadState.Unavailable, []);
        }
        finally
        {
            _ = activity?.SetTag("portal.outcome", outcome.ToString());
            await telemetry.ObserveAsync(
                channel.Host.Id,
                icon,
                PortalAudience.Self,
                outcome,
                Stopwatch.GetElapsedTime(started)
            );
        }
    }

    private async Task<ImmutableArray<PortalPersonalItem>> ReadBoundAsync(
        PortalChannel channel,
        AuthenticatedSession session,
        PortalSelfOwner owner,
        CancellationToken ct
    )
    {
        var bound = await access.BindSelfAsync(channel, ViewerPortalAccess.Identify(session), ct);
        var viewer = bound.Match<PortalViewer?>(
            static _ => null,
            static value => value.Viewer,
            static value => value.Viewer,
            static _ => null,
            static _ => null,
            static _ => null
        );
        return viewer is null
            ? []
            : await (
                owner switch
                {
                    PortalSelfOwner.Passport => PassportAsync(channel, viewer, ct),
                    PortalSelfOwner.Queue => QueueAsync(channel, viewer, ct),
                    PortalSelfOwner.Requests => RequestsAsync(channel, session, ct),
                    PortalSelfOwner.Bingo => BingoAsync(channel, viewer, ct),
                }
            );
    }

    private async Task<ImmutableArray<PortalPersonalItem>> PassportAsync(
        PortalChannel channel,
        PortalViewer viewer,
        CancellationToken ct
    )
    {
        if (!channel.PublicFeatures.Contains(HostFeatureFlags.ViewerPassports))
        {
            return [];
        }
        var passport = await passports.GetSelfSummaryAsync(
            viewer.Host.Id,
            new ViewerPassportIdentity(viewer.TwitchUserId, viewer.Login, viewer.DisplayName),
            ct
        );
        if (
            passport is null
            || passport.HostId != viewer.Host.Id
            || passport.TwitchUserId != viewer.TwitchUserId
        )
        {
            throw new InvalidOperationException("The passport summary is unavailable.");
        }
        var items = ImmutableArray.CreateBuilder<PortalPersonalItem>();
        if (channel.PublicFeatures.Contains(HostFeatureFlags.Points))
        {
            items.Add(
                new(
                    "Points",
                    passport.Points,
                    "Your channel points",
                    new("Open standings", Route("points/leaderboard", channel)),
                    passport.PointsRank
                )
            );
        }
        items.Add(
            new(
                "Passport",
                passport.Visibility.ToString(),
                passport.ProfileLine,
                new("Edit passport", $"/passports/{Uri.EscapeDataString(channel.Host.Login)}/me")
            )
        );
        return items.ToImmutable();
    }

    private async Task<ImmutableArray<PortalPersonalItem>> QueueAsync(
        PortalChannel channel,
        PortalViewer viewer,
        CancellationToken ct
    )
    {
        if (!channel.PublicFeatures.Contains(HostFeatureFlags.PlayWithViewers))
        {
            return [];
        }
        var candidates = await queues.GetPublicDestinationsAsync(
            channel.Host.Id,
            PortalSummaryBounds.Items,
            ct
        );
        var items = ImmutableArray.CreateBuilder<PortalPersonalItem>();
        foreach (var queue in candidates)
        {
            var result = await queues.GetSelfPositionAsync(
                viewer.Host.Id,
                queue.Slug,
                viewer.TwitchUserId,
                ct
            );
            _ = result.Match(
                value =>
                {
                    items.Add(
                        new(
                            "Play queue",
                            value.Value.Status == PlayQueueEntryStatus.Selected
                                ? "In the party"
                                : $"#{value.Value.Position}",
                            queue.Name,
                            new(
                                "Open the queue",
                                $"{Route("queues", channel)}/{Uri.EscapeDataString(queue.Slug)}"
                            )
                        )
                    );
                    return true;
                },
                static _ => false
            );
        }
        return items.ToImmutable();
    }

    private async Task<ImmutableArray<PortalPersonalItem>> RequestsAsync(
        PortalChannel channel,
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        if (
            !channel.PublicFeatures.Contains(HostFeatureFlags.RequestBoards)
            || RequestActor.FromSession(session) is not { } actor
        )
        {
            return [];
        }
        var candidates = await requests.GetPublicDestinationsAsync(
            channel.Host.Id,
            PortalSummaryBounds.Items,
            ct
        );
        var items = ImmutableArray.CreateBuilder<PortalPersonalItem>();
        foreach (var board in candidates)
        {
            var self = await requests.GetSelfAsync(channel.Host.Id, board.Slug, actor, [], ct);
            if (self is not null)
            {
                items.Add(
                    new(
                        "Requests",
                        $"{self.ActiveSubmissionCount} active",
                        $"{self.VotesRemaining} votes left · {board.Title}",
                        new(
                            "Open the board",
                            $"{Route("requests", channel)}/{Uri.EscapeDataString(board.Slug)}"
                        )
                    )
                );
            }
        }
        return items.ToImmutable();
    }

    private async Task<ImmutableArray<PortalPersonalItem>> BingoAsync(
        PortalChannel channel,
        PortalViewer viewer,
        CancellationToken ct
    ) =>
        !channel.PublicFeatures.Contains(HostFeatureFlags.Bingo)
            ? []
            : (await bingo.GetSelfCardAsync(viewer.Host.Id, viewer.TwitchUserId, ct)).Match<
                ImmutableArray<PortalPersonalItem>
            >(
                value =>
                    [
                        new(
                            "Bingo",
                            value.Card.AssignmentName,
                            $"{value.Card.MarkedSquares} of {value.Card.TotalSquares} squares marked",
                            new("Open your card", Route("bingo", channel))
                        ),
                    ],
                static _ => [],
                static _ => []
            );

    private static string Route(string prefix, PortalChannel channel) =>
        $"/{prefix}/{Uri.EscapeDataString(channel.Host.Login)}";
}
