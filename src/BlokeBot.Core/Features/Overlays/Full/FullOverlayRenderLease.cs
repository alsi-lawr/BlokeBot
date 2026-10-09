using System.Collections.Immutable;
using System.Threading.Channels;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Overlays.Full;

internal readonly record struct FullOverlayMediaReference(Guid AssetId, int Revision);

internal sealed record FullOverlayPluginResource(Guid Id, PluginWidgetEndpoint Endpoint);

internal sealed class FullOverlayPreviewCandidate(
    AuthenticatedSession session,
    OverlayManagementActor actor,
    FullOverlayRevision revision,
    FullOverlayDocument document,
    FullOverlayDataMode mode,
    DateTimeOffset expiresAt
)
{
    private long _expiresAt = expiresAt.UtcTicks;
    private int _revoked;
    internal bool IsRevoked => Volatile.Read(ref _revoked) != 0;

    internal void Revoke() => Interlocked.Exchange(ref _revoked, 1);

    internal AuthenticatedSession Session { get; } = session;
    internal OverlayManagementActor Actor { get; } = actor;
    internal FullOverlayRevision Revision { get; } = revision;
    internal FullOverlayDocument Document { get; } = document;
    internal FullOverlayDataMode Mode { get; } = mode;
    internal DateTimeOffset ExpiresAt
    {
        get => new(Interlocked.Read(ref _expiresAt), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _expiresAt, value.UtcTicks);
    }
}

internal sealed class FullOverlayRenderLease : IDisposable
{
    internal FullOverlayRenderLease(
        Guid id,
        PublishedFullOverlay? selected,
        FullOverlayPreviewCandidate? preview,
        OverlayLiveCoordinator.FullConnection? connection,
        DateTimeOffset expiresAt
    )
    {
        Id = id;
        Selected = selected;
        Preview = preview;
        Connection = connection;
        ExpiresAt = expiresAt;
        Cancellation = _lifetime.Token;
    }

    private readonly Channel<byte> _refresh = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        }
    );
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    private long _expiresAt;
    internal Guid Id { get; }
    internal PublishedFullOverlay? Selected { get; set; }
    internal FullOverlayPreviewCandidate? Preview { get; }
    internal OverlayLiveCoordinator.FullConnection? Connection { get; }
    internal Guid ConnectionId => Connection?.Id ?? Id;
    internal ImmutableHashSet<FullOverlayMediaReference> DeclaredMedia { get; set; } = [];
    internal ImmutableDictionary<Guid, FullOverlayPluginResource> DeclaredPlugins { get; set; } =
        ImmutableDictionary<Guid, FullOverlayPluginResource>.Empty;
    internal DateTimeOffset ExpiresAt
    {
        get => new(Interlocked.Read(ref _expiresAt), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _expiresAt, value.UtcTicks);
    }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    internal int HostId => Selected?.HostId ?? Preview!.Actor.HostId;
    internal Guid OverlayId => Selected?.OverlayId ?? Preview!.Document.Id;
    internal ChannelReader<byte> Refresh => Connection?.Refresh ?? _refresh.Reader;
    internal CancellationToken Cancellation { get; }

    internal void Wake()
    {
        if (Connection is not null)
        {
            Connection.Wake();
        }
        else
        {
            _ = _refresh.Writer.TryWrite(0);
        }
    }

    internal void Subscribe(IDisposable subscription) => _subscriptions.Add(subscription);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _ = _refresh.Writer.TryComplete();
    }
}
