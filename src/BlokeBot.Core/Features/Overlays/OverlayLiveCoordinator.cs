using System.Collections.Concurrent;
using System.Threading.Channels;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class OverlayLiveCoordinator(
    OverlayServerEpoch serverEpoch,
    IOverlayStateProvider stateProvider,
    TimeProvider timeProvider,
    EventBus<AppEventKind> events,
    ILogger<OverlayLiveCoordinator> logger
)
    : IHostedService,
        IOverlayLivePublisher,
        IOverlayLivePresence,
        IOverlayCueTransport,
        IAsyncDisposable,
        IGuessingChangeObserver
{
    private const int _connectionQueueCapacity = 16;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, OverlayPublicationSlot> _publicationSlots = new();
    private readonly object _connectionsGate = new();
    private readonly Dictionary<Guid, OverlayLiveConnection> _connections = [];
    private readonly Dictionary<OverlayIdentity, PresenceState> _presence = [];
    private readonly Dictionary<Guid, long> _sequences = [];
    private readonly Dictionary<OverlayIdentity, GuessingOverlayPhase> _guessingPhases = [];
    private IDisposable? _overlayChangesSubscription;
    private long _generation;
    private int _disposeState;

    internal long Generation => Volatile.Read(ref _generation);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _overlayChangesSubscription = events.Subscribe(
            [AppEventKind.OverlaysChanged, AppEventKind.HostedChannelsChanged],
            ObserverIdentity.For(typeof(OverlayLiveCoordinator)),
            (_, _) =>
            {
                InvalidateAllConnections();
                return ValueTask.CompletedTask;
            }
        );
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _overlayChangesSubscription?.Dispose();
        _overlayChangesSubscription = null;
        _stopping.Cancel();
        InvalidateAllConnections();
        return Task.CompletedTask;
    }

    public void PublishState(ResolvedOverlayInstance instance)
    {
        QueuePublication(instance, OverlayLivePublicationKind.State);
    }

    public void PublishTest(ResolvedOverlayInstance instance)
    {
        QueuePublication(instance, OverlayLivePublicationKind.Test);
    }

    void IOverlayCueTransport.Start(ResolvedOverlayInstance target, OverlayCuePlaybackPlan plan)
    {
        PublishCueMessage(
            target,
            (sequence, occurredAtUtc) =>
                new OverlayLiveTransportMessage.Cue(
                    new CuePlaybackLiveEnvelope
                    {
                        ServerEpoch = serverEpoch.Value,
                        Sequence = sequence,
                        OccurredAtUtc = occurredAtUtc,
                        Payload = new CuePlaybackLivePayload
                        {
                            RunId = plan.RunId,
                            DurationMilliseconds = plan.DurationMilliseconds,
                            Layers = plan.Layers.Select(ToPayload).ToArray(),
                        },
                    }
                )
        );
    }

    void IOverlayCueTransport.Stop(ResolvedOverlayInstance target, Guid runId)
    {
        PublishCueMessage(
            target,
            (sequence, occurredAtUtc) =>
                new OverlayLiveTransportMessage.CueStop(
                    new CuePlaybackStopLiveEnvelope
                    {
                        ServerEpoch = serverEpoch.Value,
                        Sequence = sequence,
                        OccurredAtUtc = occurredAtUtc,
                        RunId = runId,
                    }
                )
        );
    }

    public OverlayConnectionPresence Read(int hostId, Guid overlayId)
    {
        lock (_connectionsGate)
        {
            return _presence.TryGetValue(new OverlayIdentity(hostId, overlayId), out var presence)
                ? presence.Snapshot()
                : EmptyPresence();
        }
    }

    public ValueTask GuessingChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedOverlayInstance[] instances;
        lock (_connectionsGate)
        {
            instances = _connections
                .Values.Where(connection =>
                    connection.IsActive
                    && connection.Instance.HostId == hostId
                    && connection.Instance.Type == BlokeBot.Persistence.Models.OverlayType.Guessing
                )
                .Select(connection => connection.Instance)
                .DistinctBy(instance => instance.OverlayId)
                .ToArray();
        }

        foreach (var instance in instances)
        {
            QueuePublication(instance, OverlayLivePublicationKind.State);
        }

        return ValueTask.CompletedTask;
    }

    internal async Task<OverlayLiveOpenResult> OpenAsync(
        ResolvedOverlayInstance instance,
        long resolvedGeneration,
        CancellationToken cancellationToken
    )
    {
        var projection = await stateProvider.ProjectAsync(instance, cancellationToken);
        if (
            projection
            is not (
                OverlaySnapshotProjection.EmptyV1
                or OverlaySnapshotProjection.GuessingV1
                or OverlaySnapshotProjection.CuePlayerV1
            )
        )
        {
            return new OverlayLiveOpenResult.Unavailable();
        }

        lock (_connectionsGate)
        {
            if (resolvedGeneration != _generation)
            {
                return new OverlayLiveOpenResult.ReauthenticationRequired();
            }

            var connection = new OverlayLiveConnection(
                Guid.NewGuid(),
                instance,
                resolvedGeneration,
                _connectionQueueCapacity
            );
            _connections.Add(connection.Id, connection);
            var presence = GetOrCreatePresence(connection.Identity);
            presence.Connected(timeProvider.GetUtcNow());
            connection.TryWrite(Baseline(instance, projection));
            return new OverlayLiveOpenResult.Opened(connection);
        }
    }

    internal void Close(OverlayLiveConnection connection)
    {
        lock (_connectionsGate)
        {
            RemoveConnection(connection, timeProvider.GetUtcNow(), complete: true);
        }
    }

    internal bool MaySend(OverlayLiveConnection connection)
    {
        lock (_connectionsGate)
        {
            return connection.IsActive
                && connection.Generation == _generation
                && _connections.ContainsKey(connection.Id);
        }
    }

    private void QueuePublication(ResolvedOverlayInstance instance, OverlayLivePublicationKind kind)
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var slot = _publicationSlots.GetOrAdd(
                instance.OverlayId,
                _ => new OverlayPublicationSlot(publication =>
                    PublishPendingAsync(publication, _stopping.Token)
                )
            );
            slot.Queue(new OverlayPublication(instance, kind));
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "An overlay live publication could not be queued for overlay {OverlayId}.",
                instance.OverlayId
            );
        }
    }

    private async Task PublishPendingAsync(
        OverlayPublication publication,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!HasConnections(publication.Instance))
            {
                return;
            }

            var projection = await stateProvider.ProjectAsync(
                publication.Instance,
                cancellationToken
            );
            if (
                projection
                is not (
                    OverlaySnapshotProjection.EmptyV1
                    or OverlaySnapshotProjection.GuessingV1
                    or OverlaySnapshotProjection.CuePlayerV1
                )
            )
            {
                logger.LogWarning(
                    "An overlay live publication had no supported projection for overlay {OverlayId}.",
                    publication.Instance.OverlayId
                );
                return;
            }

            PublishProjection(publication, projection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "An overlay live publication failed for overlay {OverlayId}.",
                publication.Instance.OverlayId
            );
        }
    }

    private bool HasConnections(ResolvedOverlayInstance instance)
    {
        lock (_connectionsGate)
        {
            return _connections.Values.Any(connection =>
                connection.IsActive
                && connection.Identity == new OverlayIdentity(instance.HostId, instance.OverlayId)
            );
        }
    }

    private void PublishProjection(
        OverlayPublication publication,
        OverlaySnapshotProjection projection
    )
    {
        lock (_connectionsGate)
        {
            var identity = new OverlayIdentity(
                publication.Instance.HostId,
                publication.Instance.OverlayId
            );
            var targets = _connections
                .Values.Where(connection =>
                    connection.IsActive
                    && connection.Generation == _generation
                    && connection.Identity == identity
                )
                .ToArray();
            if (targets.Length == 0)
            {
                return;
            }

            var nextSequence = CurrentSequence(publication.Instance.OverlayId) + 1;
            _sequences[publication.Instance.OverlayId] = nextSequence;
            var message = Event(publication, projection, identity, nextSequence);
            foreach (var connection in targets)
            {
                if (!connection.TryWrite(message))
                {
                    RemoveConnection(connection, timeProvider.GetUtcNow(), complete: false);
                    connection.Invalidate(
                        new OverlayLiveControlEnvelope
                        {
                            ServerEpoch = serverEpoch.Value,
                            Sequence = nextSequence,
                            EventType = "resync",
                            OccurredAtUtc = timeProvider.GetUtcNow(),
                        }
                    );
                }
            }
        }
    }

    private OverlayLiveTransportMessage Baseline(
        ResolvedOverlayInstance instance,
        OverlaySnapshotProjection projection
    )
    {
        var sequence = CurrentSequence(instance.OverlayId);
        var occurredAtUtc = timeProvider.GetUtcNow();
        return projection switch
        {
            OverlaySnapshotProjection.EmptyV1 => new OverlayLiveTransportMessage.Baseline(
                new EmptyV1OverlayLiveBaselineEnvelope
                {
                    ServerEpoch = serverEpoch.Value,
                    Sequence = sequence,
                    OccurredAtUtc = occurredAtUtc,
                }
            ),
            OverlaySnapshotProjection.GuessingV1 guessing => GuessingBaseline(
                instance,
                guessing.Snapshot,
                sequence,
                occurredAtUtc
            ),
            OverlaySnapshotProjection.CuePlayerV1 =>
                new OverlayLiveTransportMessage.CuePlayerBaseline(
                    new CuePlayerV1OverlayLiveBaselineEnvelope
                    {
                        ServerEpoch = serverEpoch.Value,
                        Sequence = sequence,
                        OccurredAtUtc = occurredAtUtc,
                    }
                ),
            _ => throw new InvalidOperationException(
                "A supported projection is required to open a live overlay."
            ),
        };
    }

    private OverlayLiveTransportMessage GuessingBaseline(
        ResolvedOverlayInstance instance,
        GuessingV1OverlaySnapshot snapshot,
        long sequence,
        DateTimeOffset occurredAtUtc
    )
    {
        _guessingPhases[new OverlayIdentity(instance.HostId, instance.OverlayId)] = snapshot
            .State
            .Phase;
        return new OverlayLiveTransportMessage.GuessingBaseline(
            new GuessingV1OverlayLiveBaselineEnvelope
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = sequence,
                OccurredAtUtc = occurredAtUtc,
                Payload = GuessingPayload(snapshot, "none"),
            }
        );
    }

    private OverlayLiveTransportMessage Event(
        OverlayPublication publication,
        OverlaySnapshotProjection projection,
        OverlayIdentity identity,
        long sequence
    )
    {
        var occurredAtUtc = timeProvider.GetUtcNow();
        return projection switch
        {
            OverlaySnapshotProjection.EmptyV1 => new OverlayLiveTransportMessage.Event(
                new EmptyV1OverlayLiveEnvelope
                {
                    ServerEpoch = serverEpoch.Value,
                    Sequence = sequence,
                    Kind = publication.Kind,
                    OccurredAtUtc = occurredAtUtc,
                }
            ),
            OverlaySnapshotProjection.GuessingV1 guessing => GuessingEvent(
                publication.Kind,
                identity,
                guessing.Snapshot,
                sequence,
                occurredAtUtc
            ),
            OverlaySnapshotProjection.CuePlayerV1 => throw new InvalidOperationException(
                "Cue players publish only typed cue transport messages."
            ),
            _ => throw new InvalidOperationException(
                "A supported projection is required for live publication."
            ),
        };
    }

    private OverlayLiveTransportMessage GuessingEvent(
        OverlayLivePublicationKind kind,
        OverlayIdentity identity,
        GuessingV1OverlaySnapshot snapshot,
        long sequence,
        DateTimeOffset occurredAtUtc
    )
    {
        var animation =
            kind is OverlayLivePublicationKind.Test
                ? "none"
                : AnimationFor(_guessingPhases.GetValueOrDefault(identity), snapshot.State.Phase);
        _guessingPhases[identity] = snapshot.State.Phase;
        return new OverlayLiveTransportMessage.GuessingEvent(
            new GuessingV1OverlayLiveEnvelope
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = sequence,
                Kind = kind,
                OccurredAtUtc = occurredAtUtc,
                Payload = GuessingPayload(snapshot, animation),
            }
        );
    }

    private static GuessingV1OverlayLivePayload GuessingPayload(
        GuessingV1OverlaySnapshot snapshot,
        string animation
    )
    {
        return new GuessingV1OverlayLivePayload
        {
            ResultDurationMilliseconds = snapshot.ResultDurationMilliseconds,
            Animation = animation,
            State = snapshot.State,
        };
    }

    private static string AnimationFor(GuessingOverlayPhase previous, GuessingOverlayPhase current)
    {
        if (
            current is GuessingOverlayPhase.Completed
            && previous is not GuessingOverlayPhase.Completed
        )
        {
            return "result";
        }
        if (current is GuessingOverlayPhase.Open && previous is GuessingOverlayPhase.NoRound)
        {
            return "entrance";
        }
        return current == previous ? "none" : "statusChange";
    }

    private void PublishCueMessage(
        ResolvedOverlayInstance target,
        Func<long, DateTimeOffset, OverlayLiveTransportMessage> message
    )
    {
        lock (_connectionsGate)
        {
            var identity = new OverlayIdentity(target.HostId, target.OverlayId);
            var connections = _connections
                .Values.Where(value =>
                    value.IsActive
                    && value.Generation == _generation
                    && value.Identity == identity
                    && value.Instance.Type == BlokeBot.Persistence.Models.OverlayType.CuePlayer
                )
                .ToArray();
            if (connections.Length == 0)
            {
                return;
            }
            var sequence = CurrentSequence(target.OverlayId) + 1;
            _sequences[target.OverlayId] = sequence;
            var publication = message(sequence, timeProvider.GetUtcNow());
            foreach (var connection in connections)
            {
                if (!connection.TryWrite(publication))
                {
                    RemoveConnection(connection, timeProvider.GetUtcNow(), complete: false);
                }
            }
        }
    }

    private static CuePlaybackLayerPayload ToPayload(OverlayCuePlaybackLayer layer)
    {
        return layer switch
        {
            OverlayCuePlaybackLayer.UploadedMedia value => new CuePlaybackLayerPayload
            {
                Kind = "uploadedMedia",
                AssetId = value.AssetId,
                ContentRevision = value.ContentRevision,
                ContentType = value.ContentType,
                MediaKind = value.ContentType == "video/mp4" ? "video" : "audio",
                Volume = value.Volume,
                Fit = value.Fit.ToString().ToLowerInvariant(),
                StartOffsetMilliseconds = value.StartOffsetMilliseconds,
                DurationMilliseconds = value.DurationMilliseconds,
                ZIndex = value.ZIndex,
                Rectangle = value.Rectangle,
            },
            OverlayCuePlaybackLayer.RemoteMedia value => new CuePlaybackLayerPayload
            {
                Kind = "remoteMedia",
                Url = value.Url.AbsoluteUri,
                MediaKind = value.MediaKind.ToString().ToLowerInvariant(),
                Volume = value.Volume,
                Fit = value.Fit.ToString().ToLowerInvariant(),
                StartOffsetMilliseconds = value.StartOffsetMilliseconds,
                DurationMilliseconds = value.DurationMilliseconds,
                ZIndex = value.ZIndex,
                Rectangle = value.Rectangle,
            },
            OverlayCuePlaybackLayer.ExternalWeb value => new CuePlaybackLayerPayload
            {
                Kind = "externalWeb",
                Url = value.Url.AbsoluteUri,
                StartOffsetMilliseconds = value.StartOffsetMilliseconds,
                DurationMilliseconds = value.DurationMilliseconds,
                ZIndex = value.ZIndex,
                Rectangle = value.Rectangle,
            },
            _ => throw new InvalidOperationException("Unsupported cue playback layer."),
        };
    }

    private void InvalidateAllConnections()
    {
        lock (_connectionsGate)
        {
            Interlocked.Increment(ref _generation);
            var disconnectedAtUtc = timeProvider.GetUtcNow();
            foreach (var connection in _connections.Values.ToArray())
            {
                var sequence = CurrentSequence(connection.Identity.OverlayId);
                RemoveConnection(connection, disconnectedAtUtc, complete: false);
                connection.Invalidate(
                    new OverlayLiveControlEnvelope
                    {
                        ServerEpoch = serverEpoch.Value,
                        Sequence = sequence,
                        EventType = "reauthenticate",
                        OccurredAtUtc = disconnectedAtUtc,
                    }
                );
            }
            _guessingPhases.Clear();
        }
    }

    private void RemoveConnection(
        OverlayLiveConnection connection,
        DateTimeOffset disconnectedAtUtc,
        bool complete
    )
    {
        if (!connection.Deactivate())
        {
            return;
        }

        _connections.Remove(connection.Id);
        GetOrCreatePresence(connection.Identity).Disconnected(disconnectedAtUtc);
        if (complete)
        {
            connection.Complete();
        }
    }

    private PresenceState GetOrCreatePresence(OverlayIdentity identity)
    {
        if (!_presence.TryGetValue(identity, out var presence))
        {
            presence = new PresenceState();
            _presence.Add(identity, presence);
        }

        return presence;
    }

    private long CurrentSequence(Guid overlayId)
    {
        return _sequences.GetValueOrDefault(overlayId);
    }

    private static OverlayConnectionPresence EmptyPresence()
    {
        return new OverlayConnectionPresence
        {
            ActiveConnectionCount = 0,
            MostRecentConnectedAtUtc = null,
            MostRecentDisconnectedAtUtc = null,
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 1)
        {
            return;
        }

        _overlayChangesSubscription?.Dispose();
        _stopping.Cancel();
        foreach (var slot in _publicationSlots.Values)
        {
            await slot.DisposeAsync();
        }
        _stopping.Dispose();
    }

    private sealed record OverlayPublication(
        ResolvedOverlayInstance Instance,
        OverlayLivePublicationKind Kind
    );

    private sealed class PresenceState
    {
        private int _activeConnectionCount;
        private DateTimeOffset? _mostRecentConnectedAtUtc;
        private DateTimeOffset? _mostRecentDisconnectedAtUtc;

        internal void Connected(DateTimeOffset connectedAtUtc)
        {
            _activeConnectionCount++;
            _mostRecentConnectedAtUtc = connectedAtUtc;
        }

        internal void Disconnected(DateTimeOffset disconnectedAtUtc)
        {
            _activeConnectionCount--;
            _mostRecentDisconnectedAtUtc = disconnectedAtUtc;
        }

        internal OverlayConnectionPresence Snapshot()
        {
            return new OverlayConnectionPresence
            {
                ActiveConnectionCount = _activeConnectionCount,
                MostRecentConnectedAtUtc = _mostRecentConnectedAtUtc,
                MostRecentDisconnectedAtUtc = _mostRecentDisconnectedAtUtc,
            };
        }
    }

    private sealed class OverlayPublicationSlot : IAsyncDisposable
    {
        private readonly Channel<OverlayPublication> _publications =
            Channel.CreateBounded<OverlayPublication>(
                new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                }
            );
        private readonly Task _worker;

        internal OverlayPublicationSlot(Func<OverlayPublication, Task> publish)
        {
            _worker = RunAsync(publish);
        }

        internal void Queue(OverlayPublication publication)
        {
            _publications.Writer.TryWrite(publication);
        }

        private async Task RunAsync(Func<OverlayPublication, Task> publish)
        {
            await foreach (var publication in _publications.Reader.ReadAllAsync())
            {
                await publish(publication);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _publications.Writer.TryComplete();
            await _worker;
        }
    }

    internal sealed class OverlayLiveConnection
    {
        private readonly Channel<OverlayLiveTransportMessage> _messages;
        private readonly object _terminalGate = new();
        private OverlayLiveControlEnvelope? _terminal;
        private int _active = 1;

        internal OverlayLiveConnection(
            Guid id,
            ResolvedOverlayInstance instance,
            long generation,
            int queueCapacity
        )
        {
            Id = id;
            Instance = instance;
            Identity = new OverlayIdentity(instance.HostId, instance.OverlayId);
            Generation = generation;
            _messages = Channel.CreateBounded<OverlayLiveTransportMessage>(
                new BoundedChannelOptions(queueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                }
            );
        }

        internal Guid Id { get; }

        internal OverlayIdentity Identity { get; }

        internal ResolvedOverlayInstance Instance { get; }

        internal long Generation { get; }

        internal bool IsActive => Volatile.Read(ref _active) == 1;

        internal ChannelReader<OverlayLiveTransportMessage> Messages => _messages.Reader;

        internal bool TryWrite(OverlayLiveTransportMessage message)
        {
            return IsActive && _messages.Writer.TryWrite(message);
        }

        internal bool Deactivate()
        {
            return Interlocked.Exchange(ref _active, 0) == 1;
        }

        internal void Invalidate(OverlayLiveControlEnvelope envelope)
        {
            lock (_terminalGate)
            {
                _terminal = envelope;
            }
            _messages.Writer.TryComplete();
        }

        internal bool TryTakeTerminal(out OverlayLiveControlEnvelope? envelope)
        {
            lock (_terminalGate)
            {
                envelope = _terminal;
                _terminal = null;
                return envelope is not null;
            }
        }

        internal void Complete()
        {
            _messages.Writer.TryComplete();
        }
    }
}

internal abstract record OverlayLiveOpenResult
{
    private OverlayLiveOpenResult() { }

    internal sealed record Opened(OverlayLiveCoordinator.OverlayLiveConnection Connection)
        : OverlayLiveOpenResult;

    internal sealed record ReauthenticationRequired : OverlayLiveOpenResult;

    internal sealed record Unavailable : OverlayLiveOpenResult;
}

internal abstract record OverlayLiveTransportMessage
{
    private OverlayLiveTransportMessage() { }

    internal sealed record Baseline(EmptyV1OverlayLiveBaselineEnvelope Envelope)
        : OverlayLiveTransportMessage;

    internal sealed record Event(EmptyV1OverlayLiveEnvelope Envelope) : OverlayLiveTransportMessage;

    internal sealed record GuessingBaseline(GuessingV1OverlayLiveBaselineEnvelope Envelope)
        : OverlayLiveTransportMessage;

    internal sealed record GuessingEvent(GuessingV1OverlayLiveEnvelope Envelope)
        : OverlayLiveTransportMessage;

    internal sealed record CuePlayerBaseline(CuePlayerV1OverlayLiveBaselineEnvelope Envelope)
        : OverlayLiveTransportMessage;

    internal sealed record Cue(CuePlaybackLiveEnvelope Envelope) : OverlayLiveTransportMessage;

    internal sealed record CueStop(CuePlaybackStopLiveEnvelope Envelope)
        : OverlayLiveTransportMessage;
}

internal sealed record OverlayIdentity(int HostId, Guid OverlayId);
