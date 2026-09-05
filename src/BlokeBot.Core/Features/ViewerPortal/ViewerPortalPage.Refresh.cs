using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

public partial class ViewerPortalPage
{
    private static readonly IReadOnlySet<PortalSelfOwner> _allSelfOwners =
        new HashSet<PortalSelfOwner>
        {
            PortalSelfOwner.Passport,
            PortalSelfOwner.Queue,
            PortalSelfOwner.Requests,
            PortalSelfOwner.Bingo,
        };
    private static readonly IReadOnlyDictionary<
        AppEventKind,
        HostFeatureFlags
    > _publicRefreshKinds = new Dictionary<AppEventKind, HostFeatureFlags>
    {
        [AppEventKind.BingoChanged] = HostFeatureFlags.Bingo,
        [AppEventKind.PlayQueuesChanged] = HostFeatureFlags.PlayWithViewers,
        [AppEventKind.BountiesChanged] = HostFeatureFlags.Bounties,
        [AppEventKind.BlokeRaidChanged] = HostFeatureFlags.CooperativeGame,
        [AppEventKind.CompetitionsChanged] = HostFeatureFlags.Competitions,
        [AppEventKind.CommunityProgressionChanged] = HostFeatureFlags.CommunityProgression,
        [AppEventKind.RequestBoardsChanged] = HostFeatureFlags.RequestBoards,
        [AppEventKind.MomentsChanged] = HostFeatureFlags.Moments,
        [AppEventKind.PointsChanged] = HostFeatureFlags.Points,
        [AppEventKind.GuessingChanged] = HostFeatureFlags.Guessing,
    };

    private void Subscribe()
    {
        DisposeSubscriptions();
        var admitted = _features.Select(value => value.Feature).ToHashSet();
        var kinds = _publicRefreshKinds
            .Where(pair => admitted.Contains(pair.Value))
            .Select(pair => pair.Key)
            .Append(AppEventKind.HostedChannelsChanged);
        foreach (var kind in kinds)
        {
            _subscriptions.Add(
                _events.SubscribeForComponentRefresh(
                    kind,
                    InvokeRefreshAsync,
                    () =>
                    {
                        _refresh.Notify(kind);
                        return Task.CompletedTask;
                    },
                    NoEventRender
                )
            );
        }
    }

    private async Task InvokeRefreshAsync(Func<Task> callback)
    {
        try
        {
            await InvokeAsync(callback);
        }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (InvalidOperationException) when (_disposed) { }
    }

    private void NoEventRender() { }

    private async Task RefreshAsync(
        IReadOnlySet<AppEventKind> kinds,
        CancellationToken connectionToken
    )
    {
        if (_channel is null || _disposed)
        {
            return;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            connectionToken,
            _route.Token
        );
        var ct = lifetime.Token;
        try
        {
            await RefreshChannelAsync(kinds, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task RefreshChannelAsync(IReadOnlySet<AppEventKind> kinds, CancellationToken ct)
    {
        var expected = _channel!.Host;
        var resolved = (
            await _access.ResolveChannelAsync(expected.Login, ct)
        ).Match<PortalChannel?>(value => value.Channel, static _ => null);
        ct.ThrowIfCancellationRequested();
        if (resolved?.Host != expected)
        {
            SetNotFound();
            StateHasChanged();
            return;
        }
        var identity = await ViewerPortalAccess.IdentifyAsync(_authenticationState);
        ct.ThrowIfCancellationRequested();
        if (identity != _identity)
        {
            _identity = identity;
            _identityRevision++;
            _personal.Clear();
            _session =
                identity is PortalIdentity.Authenticated && _authenticationState is not null
                    ? AuthenticatedSession.FromPrincipal((await _authenticationState).User)
                    : AuthenticatedSession.Anonymous;
        }
        var all = kinds.Contains(AppEventKind.HostedChannelsChanged);
        if (all)
        {
            _channel = resolved;
            foreach (
                var feature in _projections
                    .Keys.Where(feature => !resolved.PublicFeatures.Contains(feature))
                    .ToArray()
            )
            {
                _ = _projections.Remove(feature);
            }
            foreach (
                var feature in _projections
                    .Where(pair => pair.Value is null)
                    .Select(pair => pair.Key)
                    .ToArray()
            )
            {
                _ = _projections.Remove(feature);
            }
        }
        var admitted = _features.Select(value => value.Feature).ToHashSet();
        var features = all
            ? admitted
            : kinds
                .Where(_publicRefreshKinds.ContainsKey)
                .Select(kind => _publicRefreshKinds[kind])
                .Where(admitted.Contains)
                .ToHashSet();
        var owners = all ? _allSelfOwners : SelfOwners(kinds);
        await ReadAsync(features, owners, ct);
        ct.ThrowIfCancellationRequested();
        Subscribe();
    }

    private static IReadOnlySet<PortalSelfOwner> SelfOwners(IReadOnlySet<AppEventKind> kinds)
    {
        var owners = new HashSet<PortalSelfOwner>();
        if (kinds.Contains(AppEventKind.PlayQueuesChanged))
        {
            _ = owners.Add(PortalSelfOwner.Queue);
        }
        if (kinds.Contains(AppEventKind.RequestBoardsChanged))
        {
            _ = owners.Add(PortalSelfOwner.Requests);
        }
        if (kinds.Contains(AppEventKind.BingoChanged))
        {
            _ = owners.Add(PortalSelfOwner.Bingo);
        }
        if (kinds.Any(kind => kind != AppEventKind.PlayQueuesChanged))
        {
            _ = owners.Add(PortalSelfOwner.Passport);
        }
        return owners;
    }

    private Task ConnectionChangedAsync(bool connected) =>
        InvokeRefreshAsync(() =>
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }
            if (!connected)
            {
                _route.Cancel();
            }
            _refresh.SetConnected(connected);
            if (connected && _route.IsCancellationRequested)
            {
                _route.Dispose();
                _route = CancellationTokenSource.CreateLinkedTokenSource(_refresh.ConnectionToken);
            }
            return Task.CompletedTask;
        });

    private void DisposeSubscriptions()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        _connection.ConnectionChanged -= ConnectionChangedAsync;
        _route.Cancel();
        _refresh.Dispose();
        _route.Dispose();
        DisposeSubscriptions();
        GC.SuppressFinalize(this);
    }
}
