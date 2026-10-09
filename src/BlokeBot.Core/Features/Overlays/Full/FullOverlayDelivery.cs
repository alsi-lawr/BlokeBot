using System.Collections.Concurrent;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayDelivery(
    FullOverlayPublishedReader publications,
    FullOverlayService documents,
    OverlayManagementAuthority authority,
    FullOverlayWidgetRegistry widgets,
    OverlayCuePlaybackService cues,
    OverlayCueService media,
    OverlayLiveCoordinator live,
    EventBus<FullOverlayEventFeedIdentity> feeds,
    TimeProvider clock,
    ILogger<FullOverlayDelivery> logger
)
{
    private readonly ConcurrentDictionary<Guid, FullOverlayPreviewCandidate> _previews = new();
    private readonly ConcurrentDictionary<Guid, FullOverlayRenderLease> _renders = new();
    private static readonly TimeSpan _idleLifetime = TimeSpan.FromMinutes(2);

    internal async Task<FullOverlayResult<Guid>> CreatePreviewAsync(
        AuthenticatedSession session,
        FullOverlayDocument document,
        FullOverlayDataMode mode,
        CancellationToken ct
    )
    {
        if (!FullOverlayDocuments.HasCoherentIdentity(document) || !Enum.IsDefined(mode))
        {
            return new FullOverlayResult<Guid>.Rejected(new(FullOverlayRejectionKind.Invalid, []));
        }
        var authorized = await authority.AuthorizeAsync(session, ct);
        if (authorized is not OverlayManagementAuthorization.Granted granted)
        {
            return new FullOverlayResult<Guid>.Rejected(
                new(FullOverlayRejectionKind.Unauthorized, [])
            );
        }
        var current = await documents.GetAsync(session, document.Id, ct);
        return current.Match<FullOverlayResult<Guid>>(
            found =>
            {
                var id = Guid.NewGuid();
                _previews[id] = new(
                    session,
                    granted.Actor,
                    found.Value.Revision,
                    document with
                    {
                        Diagnostics = [],
                    },
                    mode,
                    clock.GetUtcNow() + _idleLifetime
                );
                ReclaimExpired();
                return new FullOverlayResult<Guid>.Succeeded(id);
            },
            rejected => new FullOverlayResult<Guid>.Rejected(rejected.Reason)
        );
    }

    internal async Task<bool> MayOpenPreviewAsync(
        Guid id,
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        if (!_previews.TryGetValue(id, out var preview) || preview.ExpiresAt <= clock.GetUtcNow())
        {
            return false;
        }
        var authorized = await authority.AuthorizeAsync(session, ct);
        return authorized is OverlayManagementAuthorization.Granted granted
            && granted.Actor == preview.Actor
            && await PreviewCurrentAsync(preview, ct);
    }

    internal async Task<FullOverlayRenderLease?> OpenPreviewAsync(
        Guid id,
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        if (
            !await MayOpenPreviewAsync(id, session, ct)
            || !_previews.TryGetValue(id, out var preview)
        )
        {
            return null;
        }
        if (preview.IsRevoked)
        {
            return null;
        }
        var lease = new FullOverlayRenderLease(
            Guid.NewGuid(),
            null,
            preview,
            null,
            clock.GetUtcNow() + _idleLifetime
        );
        _ = Register(lease, preview.Document);
        if (preview.IsRevoked)
        {
            Close(lease);
            return null;
        }
        return lease;
    }

    internal FullOverlayRenderLease OpenPublished(PublishedFullOverlay selected)
    {
        var connection = live.OpenFull(selected);
        var lease = new FullOverlayRenderLease(
            Guid.NewGuid(),
            selected,
            null,
            connection,
            clock.GetUtcNow() + _idleLifetime
        );
        return Register(lease, selected.Document);
    }

    private FullOverlayRenderLease Register(
        FullOverlayRenderLease lease,
        FullOverlayDocument document
    )
    {
        _renders[lease.Id] = lease;
        foreach (var binding in FullOverlayEventFeeds.Bindings(document))
        {
            lease.Subscribe(
                feeds.Subscribe(
                    [new(lease.HostId, lease.OverlayId, binding.BindingId)],
                    ObserverIdentity.Named($"full-render-{lease.Id:N}"),
                    (_, _) =>
                    {
                        lease.Wake();
                        return ValueTask.CompletedTask;
                    }
                )
            );
        }
        lease.Wake();
        ReclaimExpired();
        return lease;
    }

    internal void Close(FullOverlayRenderLease lease)
    {
        _ = _renders.TryRemove(lease.Id, out _);
        lease.Dispose();
        if (lease.Connection is { } connection)
        {
            live.CloseFull(connection);
        }
    }

    internal async Task<bool> RefreshAuthorityAsync(
        FullOverlayRenderLease lease,
        CancellationToken ct
    )
    {
        if (lease.IsDisposed || lease.ExpiresAt <= clock.GetUtcNow())
        {
            return false;
        }
        if (lease.Selected is { } previous)
        {
            var selected = await publications.ResolveAsync(previous.HostId, previous.OverlayId, ct);
            if (
                selected is null
                || selected.Generation != previous.Generation
                || lease.Connection is null
                || !live.IsCurrent(lease.Connection)
            )
            {
                return false;
            }
            lease.Selected = selected;
        }
        else if (!await PreviewCurrentAsync(lease.Preview!, ct))
        {
            return false;
        }
        lease.ExpiresAt = clock.GetUtcNow() + _idleLifetime;
        if (lease.Preview is { } preview)
        {
            preview.ExpiresAt = lease.ExpiresAt;
        }
        return !lease.IsDisposed;
    }

    private async Task<bool> PreviewCurrentAsync(
        FullOverlayPreviewCandidate preview,
        CancellationToken ct
    )
    {
        var authorized = await authority.AuthorizeAsync(preview.Session, ct);
        return !preview.IsRevoked
            && authorized is OverlayManagementAuthorization.Granted granted
            && granted.Actor == preview.Actor
            && (await documents.GetAsync(preview.Session, preview.Document.Id, ct)).Match(
                found => found.Value.Revision == preview.Revision,
                _ => false
            );
    }

    private void ReclaimExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var pair in _previews.Where(pair => pair.Value.ExpiresAt <= now))
        {
            _ = _previews.TryRemove(pair.Key, out _);
        }
        foreach (var lease in _renders.Values.Where(lease => lease.ExpiresAt <= now))
        {
            Close(lease);
        }
    }
}
