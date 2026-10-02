using System.Threading.Channels;
using BlokeBot.Core.Features.Overlays.Full;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayLiveCoordinator
{
    private readonly Dictionary<Guid, FullConnection> _fullConnections = [];
    internal event Action<int?>? FullSourceChanged;

    internal FullConnection OpenFull(PublishedFullOverlay selected)
    {
        lock (_connectionsGate)
        {
            var connection = new FullConnection(Guid.NewGuid(), selected);
            _fullConnections.Add(connection.Id, connection);
            GetOrCreatePresence(new(selected.HostId, selected.OverlayId))
                .Connected(timeProvider.GetUtcNow());
            connection.Wake();
            return connection;
        }
    }

    internal void CloseFull(FullConnection connection)
    {
        lock (_connectionsGate)
        {
            if (!_fullConnections.Remove(connection.Id))
            {
                return;
            }
            GetOrCreatePresence(new(connection.Selected.HostId, connection.Selected.OverlayId))
                .Disconnected(timeProvider.GetUtcNow());
            connection.Complete();
        }
    }

    internal bool IsCurrent(FullConnection connection)
    {
        lock (_connectionsGate)
        {
            return _fullConnections.ContainsKey(connection.Id);
        }
    }

    private void CloseAllFullConnections()
    {
        lock (_connectionsGate)
        {
            foreach (var connection in _fullConnections.Values.ToArray())
            {
                CloseFull(connection);
            }
        }
    }

    internal void WakeFullConnections(int? hostId = null)
    {
        FullSourceChanged?.Invoke(hostId);
        lock (_connectionsGate)
        {
            foreach (
                var connection in _fullConnections.Values.Where(connection =>
                    hostId is null || connection.Selected.HostId == hostId
                )
            )
            {
                connection.Wake();
            }
        }
    }

    void IOverlayCueTransport.Start(OverlayCueTarget target, OverlayCuePlaybackPlan plan) =>
        _ = target.Match(
            simple =>
            {
                StartSimpleCue(simple.Instance, plan);
                return true;
            },
            full =>
            {
                WakeFullConnections(full.HostId);
                return true;
            }
        );

    void IOverlayCueTransport.Stop(OverlayCueTarget target, Guid runId) =>
        _ = target.Match(
            simple =>
            {
                StopSimpleCue(simple.Instance, runId);
                return true;
            },
            full =>
            {
                WakeFullConnections(full.HostId);
                return true;
            }
        );

    internal sealed class FullConnection(Guid id, PublishedFullOverlay selected)
    {
        private readonly Channel<byte> _refresh = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            }
        );
        internal Guid Id { get; } = id;
        internal PublishedFullOverlay Selected { get; } = selected;
        internal ChannelReader<byte> Refresh => _refresh.Reader;

        internal void Wake() => _ = _refresh.Writer.TryWrite(0);

        internal void Complete() => _refresh.Writer.TryComplete();
    }
}
