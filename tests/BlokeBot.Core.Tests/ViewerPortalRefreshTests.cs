using BlokeBot.Core.Features.ViewerPortal;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalRefreshTests
{
    [Test]
    public async Task EventKinds_CoalesceWithoutLosingPendingOwnersOrBypassingCadence()
    {
        var clock = new ManualTestTimeProvider(DateTimeOffset.UtcNow);
        var reads = new List<IReadOnlySet<AppEventKind>>();
        using var refresh = new PortalRefreshCoordinator(
            clock,
            (kinds, _) =>
            {
                reads.Add(kinds);
                return Task.CompletedTask;
            }
        );
        refresh.Notify(AppEventKind.BingoChanged);
        await refresh.Completion;
        refresh.Notify(AppEventKind.PointsChanged);
        refresh.Notify(AppEventKind.RequestBoardsChanged);
        refresh.Notify(AppEventKind.PointsChanged);
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(9));
        reads.Count.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(1));
        await refresh.Completion;
        reads.Count.ShouldBe(2);
        reads[1]
            .SetEquals([AppEventKind.PointsChanged, AppEventKind.RequestBoardsChanged])
            .ShouldBeTrue();
    }

    [Test]
    public async Task FrameworkDisconnect_CancelsReadsAndReconnectRevalidatesInsideExistingCadence()
    {
        var clock = new ManualTestTimeProvider(DateTimeOffset.UtcNow);
        var started = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reads = new List<IReadOnlySet<AppEventKind>>();
        using var refresh = new PortalRefreshCoordinator(
            clock,
            async (kinds, ct) =>
            {
                reads.Add(kinds);
                if (reads.Count == 1)
                {
                    started.SetResult(ct);
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
            }
        );
        var handler = new PortalCircuitConnection();
        handler.ConnectionChanged += connected =>
        {
            refresh.SetConnected(connected);
            return Task.CompletedTask;
        };
        refresh.Notify(AppEventKind.BingoChanged);
        var firstToken = await started.Task;
        refresh.Notify(AppEventKind.PlayQueuesChanged);
        await handler.OnConnectionDownAsync(null!, default);
        await refresh.Completion;
        firstToken.IsCancellationRequested.ShouldBeTrue();
        await handler.OnConnectionUpAsync(null!, default);
        _ = await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(9));
        reads.Count.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(1));
        await refresh.Completion;
        reads[1]
            .SetEquals([AppEventKind.HostedChannelsChanged, AppEventKind.PlayQueuesChanged])
            .ShouldBeTrue();
        refresh.Notify(AppEventKind.RequestBoardsChanged);
        _ = await clock.WaitForTimerRegistrationAsync();
        await handler.OnCircuitClosedAsync(null!, default);
        refresh.Dispose();
        refresh.Notify(AppEventKind.RequestBoardsChanged);
        clock.Advance(TimeSpan.FromMinutes(1));
        reads.Count.ShouldBe(2);
    }
}
