using BlokeBot.Core.Features.Overlays;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayLiveCoordinatorTests
{
    [Test]
    public async Task Suppression_DiscardsCapturedPreFenceProjectionWithoutSequenceGap()
    {
        var provider = new BlockingCapturedEventFeedProvider();
        await using var coordinator = Coordinator(provider);
        await coordinator.StartAsync(CancellationToken.None);
        var instance = EventFeedInstance();
        var connection = await OpenAsync(coordinator, instance);
        _ = (
            await ReadAsync(connection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.EventFeedBaseline>();

        try
        {
            coordinator.PublishState(instance);
            await provider.ActiveProjectionCaptured;
            ((IOverlayEventFeedLivePublisher)coordinator).PublishSuppression(
                instance,
                new EventFeedStatePresentation(null, [])
            );
            var clear = (
                await ReadAsync(connection)
            ).ShouldBeOfType<OverlayLiveTransportMessage.EventFeedEvent>();
            clear.Envelope.Sequence.ShouldBe(1);
            clear.Envelope.Payload.Animation.ShouldBe("none");
            clear.Envelope.Payload.State.Active.ShouldBeNull();

            provider.ReleaseCapturedProjection();
            coordinator.PublishState(instance);
            var current = (
                await ReadAsync(connection)
            ).ShouldBeOfType<OverlayLiveTransportMessage.EventFeedEvent>();
            current.Envelope.Sequence.ShouldBe(2);
            current.Envelope.Payload.State.Active.ShouldBeNull();
            connection.Messages.TryRead(out _).ShouldBeFalse();
        }
        finally
        {
            provider.ReleaseCapturedProjection();
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task RapidPublications_CoalesceLatestPendingStateWithoutSequenceGap()
    {
        var provider = new BlockingSecondProjectionProvider();
        await using var coordinator = Coordinator(provider);
        await coordinator.StartAsync(CancellationToken.None);
        var instance = Instance();
        var connection = await OpenAsync(coordinator, instance);
        await ReadBaselineAsync(connection);

        coordinator.PublishState(instance);
        await provider.SecondProjectionEntered;
        coordinator.PublishTest(instance);
        coordinator.PublishState(instance);
        provider.ReleaseSecondProjection();

        var first = await ReadEventAsync(connection);
        var second = await ReadEventAsync(connection);
        first.Envelope.Sequence.ShouldBe(1);
        first.Envelope.Kind.ShouldBe(OverlayLivePublicationKind.State);
        second.Envelope.Sequence.ShouldBe(2);
        second.Envelope.Kind.ShouldBe(OverlayLivePublicationKind.State);
        coordinator.Read(instance.HostId, instance.OverlayId).ActiveConnectionCount.ShouldBe(1);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ProjectionFailure_IsolatedAndDoesNotAdvancePublishedSequence()
    {
        var provider = new FailingSecondProjectionProvider();
        await using var coordinator = Coordinator(provider);
        await coordinator.StartAsync(CancellationToken.None);
        var instance = Instance();
        var connection = await OpenAsync(coordinator, instance);
        await ReadBaselineAsync(connection);

        coordinator.PublishState(instance);
        await provider.FailedProjectionObserved;
        coordinator.PublishTest(instance);

        var publication = await ReadEventAsync(connection);
        publication.Envelope.Sequence.ShouldBe(1);
        publication.Envelope.Kind.ShouldBe(OverlayLivePublicationKind.Test);
        await coordinator.StopAsync(CancellationToken.None);
    }

    private static OverlayLiveCoordinator Coordinator(IOverlayStateProvider provider) =>
        new OverlayLiveCoordinator(
            new OverlayServerEpoch(),
            provider,
            TimeProvider.System,
            TestEventBus.Create<AppEventKind>(),
            NullLogger<OverlayLiveCoordinator>.Instance
        );

    private static ResolvedOverlayInstance Instance() =>
        new ResolvedOverlayInstance(
            71,
            Guid.NewGuid(),
            BlokeBot.Persistence.Models.OverlayType.Empty,
            new OverlayConfiguration.EmptyV1(),
            new OverlayRevision(9)
        );

    private static ResolvedOverlayInstance EventFeedInstance() =>
        new(
            71,
            Guid.NewGuid(),
            BlokeBot.Persistence.Models.OverlayType.EventFeed,
            OverlayConfiguration.EventFeedV1.Default,
            new OverlayRevision(9)
        );

    private static async Task<OverlayLiveCoordinator.OverlayLiveConnection> OpenAsync(
        OverlayLiveCoordinator coordinator,
        ResolvedOverlayInstance instance
    )
    {
        var opened = await coordinator.OpenAsync(
            instance,
            coordinator.Generation,
            CancellationToken.None
        );
        return opened.ShouldBeOfType<OverlayLiveOpenResult.Opened>().Connection;
    }

    private static async Task ReadBaselineAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = (
            await connection.Messages.ReadAsync(timeout.Token)
        ).ShouldBeOfType<OverlayLiveTransportMessage.Baseline>();
    }

    private static async Task<OverlayLiveTransportMessage.Event> ReadEventAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return (
            await connection.Messages.ReadAsync(timeout.Token)
        ).ShouldBeOfType<OverlayLiveTransportMessage.Event>();
    }

    private static async Task<OverlayLiveTransportMessage> ReadAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await connection.Messages.ReadAsync(timeout.Token);
    }

    private static OverlaySnapshotProjection Projection(ResolvedOverlayInstance instance) =>
        new OverlaySnapshotProjection.EmptyV1(
            new EmptyV1OverlaySnapshot
            {
                ServerEpoch = Guid.Parse("1c23f9b8-9367-477c-bb20-08a48246840b"),
                Sequence = instance.Revision.Value,
                GeneratedAtUtc = DateTimeOffset.UnixEpoch,
            }
        );

    private sealed class BlockingCapturedEventFeedProvider : IOverlayStateProvider
    {
        private readonly TaskCompletionSource _activeProjectionCaptured = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _projectionCount;

        internal Task ActiveProjectionCaptured => _activeProjectionCaptured.Task;

        public async Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectionCount = Interlocked.Increment(ref _projectionCount);
            if (projectionCount == 2)
            {
                var captured = Projection(instance, active: true);
                _ = _activeProjectionCaptured.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
                return captured;
            }
            return Projection(instance, active: false);
        }

        internal void ReleaseCapturedProjection() => _release.TrySetResult();

        private static OverlaySnapshotProjection Projection(
            ResolvedOverlayInstance instance,
            bool active
        ) =>
            new OverlaySnapshotProjection.EventFeedV1(
                new EventFeedV1OverlaySnapshot
                {
                    ServerEpoch = Guid.Parse("11d40a78-9ff2-4ac7-b1d8-418078eed571"),
                    Sequence = instance.Revision.Value,
                    GeneratedAtUtc = DateTimeOffset.UnixEpoch,
                    Animation = "none",
                    State = new EventFeedStatePresentation(
                        active
                            ? new EventFeedCardPresentation(
                                19,
                                "achievementCompletion",
                                "high",
                                "Achievement unlocked",
                                "Viewer unlocked Trailblazer",
                                DateTimeOffset.UnixEpoch,
                                DateTimeOffset.UnixEpoch.AddSeconds(8)
                            )
                            : null,
                        []
                    ),
                }
            );
    }

    private sealed class BlockingSecondProjectionProvider : IOverlayStateProvider
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _calls;

        internal Task SecondProjectionEntered => _entered.Task;

        public async Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                _ = _entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return Projection(instance);
        }

        internal void ReleaseSecondProjection() => _release.TrySetResult();
    }

    private sealed class FailingSecondProjectionProvider : IOverlayStateProvider
    {
        private readonly TaskCompletionSource _failureObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _calls;

        internal Task FailedProjectionObserved => _failureObserved.Task;

        public Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _calls) == 2)
            {
                _ = _failureObserved.TrySetResult();
                return Task.FromException<OverlaySnapshotProjection>(
                    new InvalidOperationException("Synthetic projection failure.")
                );
            }

            return Task.FromResult(Projection(instance));
        }
    }
}
