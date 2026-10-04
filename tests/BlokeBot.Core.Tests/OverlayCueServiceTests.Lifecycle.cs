using System.Collections.Concurrent;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class OverlayCueServiceTests
{
    [Test]
    public async Task Lifecycle_FromActualQueueAndServerCompletion_RetainsIdentitiesWithoutConfirmationClaims()
    {
        var observer = new CueNotices();
        await using var f = await Fixture.CreateAsync(lifecycle: observer);
        var (target, cue) = await f.SeedPlaybackAsync(durationMilliseconds: 100);
        f.Presence.Connected = true;
        await f.Playback.StartAsync(CancellationToken.None);
        var first = (
            await f.Playback.AdmitAsync(
                Request(f.HostId, target, cue, OverlayCueQueuePolicy.Enqueue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        var queued = (
            await f.Playback.AdmitAsync(
                Request(f.HostId, target, cue, OverlayCueQueuePolicy.Enqueue),
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Queued>();
        observer
            .Notices.Select(n => n.Kind)
            .ShouldBe([OverlayCueLifecycleKind.Started, OverlayCueLifecycleKind.Queued]);
        f.Clock.Advance(TimeSpan.FromMilliseconds(1250));
        (await f.Transport.ReadStoppedAsync()).ShouldBe(first.RunId);
        _ = await f.Transport.ReadStartedAsync();
        _ = await f.Transport.ReadStartedAsync();
        await observer.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var derived = observer.Notices.Single(n =>
            n.RunId == first.RunId && n.Kind == OverlayCueLifecycleKind.Finished
        );
        derived.Outcome.ShouldBe(OverlayCueLifecycleOutcome.TimeDerivedEndUnconfirmed);
        derived.CueId.ShouldBe(cue);
        derived.TargetId.ShouldBe(target);
        derived.HostId.ShouldBe(f.HostId);
        _ = await f.Playback.CompleteAsync(f.HostId, target, queued.RunId, CancellationToken.None);
        _ = await f.Playback.CompleteAsync(f.HostId, target, queued.RunId, CancellationToken.None);
        observer
            .Notices.Count(n =>
                n.RunId == queued.RunId && n.Kind == OverlayCueLifecycleKind.Finished
            )
            .ShouldBe(1);
        observer
            .Notices.Last()
            .Outcome.ShouldBe(OverlayCueLifecycleOutcome.BrowserReportedEndUnverified);
    }

    private sealed class CueNotices : IOverlayCueLifecycleObserver
    {
        internal ConcurrentQueue<OverlayCueLifecycleNotice> Notices { get; } = new();
        internal TaskCompletionSource Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CueChangedAsync(OverlayCueLifecycleNotice notice, CancellationToken ct)
        {
            Notices.Enqueue(notice);
            if (notice.Kind == OverlayCueLifecycleKind.Finished)
            {
                _ = Finished.TrySetResult();
            }
            return Task.CompletedTask;
        }
    }
}
