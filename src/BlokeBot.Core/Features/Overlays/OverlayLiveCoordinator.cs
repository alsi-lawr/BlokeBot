using System.Collections.Concurrent;
using System.Threading.Channels;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class OverlayLiveCoordinator(
    OverlayServerEpoch serverEpoch,
    IOverlayStateProvider stateProvider,
    TimeProvider timeProvider,
    EventBus<AppEventKind> events,
    ILogger<OverlayLiveCoordinator> logger
) : IHostedService, IOverlayLivePublisher, IOverlayLivePresence, IAsyncDisposable
{
    private const int _connectionQueueCapacity = 16;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, OverlayPublicationSlot> _publicationSlots = new();
    private readonly object _connectionsGate = new();
    private readonly Dictionary<Guid, OverlayLiveConnection> _connections = [];
    private readonly Dictionary<OverlayIdentity, PresenceState> _presence = [];
    private readonly Dictionary<Guid, long> _sequences = [];
    private IDisposable? _overlayChangesSubscription;
    private long _generation;
    private int _disposeState;

    internal long Generation => Volatile.Read(ref _generation);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _overlayChangesSubscription = events.Subscribe(
            AppEventKind.OverlaysChanged,
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

    public OverlayConnectionPresence Read(int hostId, Guid overlayId)
    {
        lock (_connectionsGate)
        {
            return _presence.TryGetValue(new OverlayIdentity(hostId, overlayId), out var presence)
                ? presence.Snapshot()
                : EmptyPresence();
        }
    }

    internal async Task<OverlayLiveOpenResult> OpenAsync(
        ResolvedOverlayInstance instance,
        long resolvedGeneration,
        CancellationToken cancellationToken
    )
    {
        var projection = await stateProvider.ProjectAsync(instance, cancellationToken);
        if (projection is not OverlaySnapshotProjection.EmptyV1)
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
                new OverlayIdentity(instance.HostId, instance.OverlayId),
                resolvedGeneration,
                _connectionQueueCapacity
            );
            _connections.Add(connection.Id, connection);
            var presence = GetOrCreatePresence(connection.Identity);
            presence.Connected(timeProvider.GetUtcNow());
            var baseline = new EmptyV1OverlayLiveBaselineEnvelope
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = CurrentSequence(instance.OverlayId),
                OccurredAtUtc = timeProvider.GetUtcNow(),
            };
            connection.TryWrite(new OverlayLiveTransportMessage.Baseline(baseline));
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
            if (projection is not OverlaySnapshotProjection.EmptyV1)
            {
                logger.LogWarning(
                    "An overlay live publication had no supported projection for overlay {OverlayId}.",
                    publication.Instance.OverlayId
                );
                return;
            }

            PublishProjection(publication);
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

    private void PublishProjection(OverlayPublication publication)
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
            var envelope = new EmptyV1OverlayLiveEnvelope
            {
                ServerEpoch = serverEpoch.Value,
                Sequence = nextSequence,
                Kind = publication.Kind,
                OccurredAtUtc = timeProvider.GetUtcNow(),
            };
            var message = new OverlayLiveTransportMessage.Event(envelope);
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
            OverlayIdentity identity,
            long generation,
            int queueCapacity
        )
        {
            Id = id;
            Identity = identity;
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
}

internal sealed record OverlayIdentity(int HostId, Guid OverlayId);
