using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Identity;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics;

namespace BlokeBot.Core.Features.ViewerPortal;

public partial class ViewerPortalPage : IDisposable
{
    [Parameter]
    public string Login { get; set; } = string.Empty;

    [CascadingParameter]
    private Task<AuthenticationState>? _authenticationState { get; set; }

    [CascadingParameter]
    private HttpContext? _httpContext { get; set; }

    [Inject]
    private ViewerPortalAccess _access { get; set; } = null!;

    [Inject]
    private ViewerPortalCatalogueService _catalogue { get; set; } = null!;

    [Inject]
    private PortalPersonalReader _personalReader { get; set; } = null!;

    [Inject]
    private IHostStreamLivenessProvider _streams { get; set; } = null!;

    [Inject]
    private EventBus<AppEventKind> _events { get; set; } = null!;

    [Inject]
    private PortalCircuitConnection _connection { get; set; } = null!;

    [Inject]
    private TimeProvider _clock { get; set; } = null!;

    [Inject]
    private NavigationManager _navigation { get; set; } = null!;
    private readonly Dictionary<HostFeatureFlags, PortalFeatureProjection?> _projections = [];
    private readonly Dictionary<PortalSelfOwner, PortalPersonalProjection> _personal = [];
    private readonly List<IDisposable> _subscriptions = [];
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private PortalRefreshCoordinator _refresh = null!;
    private CancellationTokenSource _route = new();
    private PortalChannel? _channel;
    private PortalIdentity _identity = new PortalIdentity.Anonymous();
    private AuthenticatedSession _session = AuthenticatedSession.Anonymous;
    private string? _routeLogin;
    private bool _notFound;
    private bool _disposed;
    private bool _personalPending;
    private bool _live;
    private string _liveness = "Loading";
    private long _identityRevision;
    private string _normalizedLogin => LoginName.Parse(Login).Value;
    private string _canonicalPath =>
        $"/channel/{Uri.EscapeDataString(_channel?.Host.Login ?? _normalizedLogin)}";
    private string _canonicalUrl =>
        new Uri(new Uri(_navigation.BaseUri), _canonicalPath).AbsoluteUri;
    private string _signInUrl =>
        $"/auth/login?start=true&returnUrl={Uri.EscapeDataString(_canonicalPath)}";
    private IReadOnlyList<PortalFeatureDescriptor> _features =>
        ViewerPortalCatalogue
            .Descriptors.Where(value =>
                value.Audience == PortalAudience.Public
                && _channel?.PublicFeatures.Contains(value.Feature) == true
                && (!_projections.TryGetValue(value.Feature, out var result) || result is not null)
            )
            .ToArray();
    private IReadOnlyList<PortalFeatureDescriptor> _highlights =>
        _features
            .Where(value =>
                !_projections.ContainsKey(value.Feature)
                || Failed(value)
                || Summary(value)?.IsActive == true
            )
            .Take(4)
            .ToArray();
    private IReadOnlyList<PortalPersonalItem> _personalItems =>
        new[]
        {
            PortalSelfOwner.Passport,
            PortalSelfOwner.Queue,
            PortalSelfOwner.Requests,
            PortalSelfOwner.Bingo,
        }
            .SelectMany(owner => _personal.TryGetValue(owner, out var value) ? value.Items : [])
            .ToArray();
    private IReadOnlyList<RecentItem> _recent =>
        _features
            .SelectMany(feature =>
                (Summary(feature)?.RecentActivity ?? []).Select(activity => new RecentItem(
                    feature.Label,
                    activity
                ))
            )
            .OrderByDescending(value => value.Activity.OccurredAtUtc)
            .ThenBy(value => value.Activity.Link.Href, StringComparer.Ordinal)
            .ThenBy(value => value.Activity.Description, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

    protected override void OnInitialized()
    {
        _refresh = new(_clock, (kinds, ct) => InvokeAsync(() => RefreshAsync(kinds, ct)));
        _connection.ConnectionChanged += ConnectionChangedAsync;
    }

    protected override async Task OnParametersSetAsync()
    {
        var identity = await ViewerPortalAccess.IdentifyAsync(_authenticationState);
        _session =
            identity is PortalIdentity.Authenticated && _authenticationState is not null
                ? AuthenticatedSession.FromPrincipal((await _authenticationState).User)
                : AuthenticatedSession.Anonymous;
        var identityChanged = identity != _identity;
        _identity = identity;
        if (identityChanged)
        {
            _identityRevision++;
            _personal.Clear();
        }
        if (_routeLogin == _normalizedLogin && _channel is not null)
        {
            if (Login != _channel.Host.Login)
            {
                NavigateToCanonical();
                return;
            }
            if (identityChanged)
            {
                var ct = _route.Token;
                try
                {
                    await ReadPersonalAsync(_allSelfOwners, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }
            return;
        }
        _route.Cancel();
        _route.Dispose();
        _route = CancellationTokenSource.CreateLinkedTokenSource(_refresh.ConnectionToken);
        _routeLogin = _normalizedLogin;
        _channel = null;
        _notFound = false;
        _projections.Clear();
        _personal.Clear();
        DisposeSubscriptions();
        var route = _route.Token;
        try
        {
            var resolved = await _access.ResolveChannelAsync(Login, route);
            route.ThrowIfCancellationRequested();
            _channel = resolved.Match<PortalChannel?>(value => value.Channel, static _ => null);
            if (_channel is null)
            {
                SetNotFound();
                return;
            }
            if (Login != _channel.Host.Login)
            {
                NavigateToCanonical();
                return;
            }
            if (_httpContext?.Response.HasStarted == false)
            {
                _httpContext.Response.Headers.CacheControl = PortalCacheScope
                    .For(_channel.Host, _identity)
                    .CacheControl;
            }
            Subscribe();
            StateHasChanged();
            await ReadAsync(
                _features.Select(value => value.Feature).ToHashSet(),
                _allSelfOwners,
                route
            );
            Subscribe();
        }
        catch (OperationCanceledException) when (route.IsCancellationRequested || _disposed) { }
    }

    private void NavigateToCanonical() =>
        _navigation.NavigateTo(
            _canonicalPath + new Uri(_navigation.Uri).Query,
            forceLoad: true,
            replace: true
        );

    private async Task ReadAsync(
        IReadOnlySet<HostFeatureFlags> features,
        IReadOnlySet<PortalSelfOwner> owners,
        CancellationToken ct
    )
    {
        await _readGate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            await Task.WhenAll(
                ReadPublicAsync(features, ct),
                ReadPersonalAsync(owners, ct),
                ReadLivenessAsync(ct)
            );
            ct.ThrowIfCancellationRequested();
            StateHasChanged();
        }
        finally
        {
            _ = _readGate.Release();
        }
    }

    private Task ReadPublicAsync(IReadOnlySet<HostFeatureFlags> features, CancellationToken ct)
    {
        var channel = _channel!;
        return Task.WhenAll(
            features.Select(async feature =>
            {
                var snapshot = await _catalogue.ReadAsync(
                    channel,
                    new PortalIdentity.Anonymous(),
                    ct,
                    new HashSet<HostFeatureFlags> { feature }
                );
                ct.ThrowIfCancellationRequested();
                if (_channel?.Host != channel.Host)
                {
                    return;
                }
                _projections[feature] = snapshot.Features.SingleOrDefault();
                StateHasChanged();
            })
        );
    }

    private async Task ReadPersonalAsync(IReadOnlySet<PortalSelfOwner> owners, CancellationToken ct)
    {
        if (_identity is not PortalIdentity.Authenticated || _channel is null)
        {
            _personal.Clear();
            return;
        }
        var channel = _channel;
        var session = _session;
        var revision = _identityRevision;
        _personalPending = true;
        await Task.WhenAll(
            owners.Select(async owner =>
            {
                var result = await _personalReader.ReadAsync(channel, session, owner, ct);
                ct.ThrowIfCancellationRequested();
                if (_channel?.Host == channel.Host && revision == _identityRevision)
                {
                    _personal[owner] = result;
                    StateHasChanged();
                }
            })
        );
        if (revision == _identityRevision)
        {
            _personalPending = false;
        }
    }

    private async Task ReadLivenessAsync(CancellationToken ct)
    {
        var host = _channel!.Host;
        var result = await _streams.GetStreamLiveness(host.Login).RunAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (_channel?.Host != host)
        {
            return;
        }
        _live = result is HostStreamLivenessOutcome.Live;
        _liveness = result switch
        {
            HostStreamLivenessOutcome.Live => "Live now",
            HostStreamLivenessOutcome.Offline => "Offline",
            HostStreamLivenessOutcome.Unavailable => "Stream status unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private void SetNotFound()
    {
        _channel = null;
        _notFound = true;
        _projections.Clear();
        _personal.Clear();
        DisposeSubscriptions();
        if (_httpContext?.Response.HasStarted == false)
        {
            if (_httpContext.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
            {
                statusPages.Enabled = false;
            }
            var response = _httpContext.Response;
            response.OnStarting(() =>
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        }
    }

    private PortalSummary? Summary(PortalFeatureDescriptor feature) =>
        _projections
            .GetValueOrDefault(feature.Feature)
            ?.Outcome.Match<PortalSummary?>(
                static value => value.Summary,
                static value => value.Summary,
                static _ => null,
                static value => value.Summary,
                static _ => null,
                static _ => null
            );

    private bool Failed(PortalFeatureDescriptor feature) =>
        _projections.GetValueOrDefault(feature.Feature)?.Outcome
            is PortalSummaryOutcome.Unavailable
                or PortalSummaryOutcome.Degraded;

    private IReadOnlyList<PortalLink> Links(PortalFeatureDescriptor feature) =>
        Summary(feature) is { } summary ? summary.Links
        : feature.GetFallbackLink(_channel!.Host, _identity) is { } fallback ? [fallback]
        : [];

    private static string Initials(string name) =>
        string.Concat(
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0]))
        );

    private string RelativeTime(DateTime utc)
    {
        var elapsed = _clock.GetUtcNow() - new DateTimeOffset(utc, TimeSpan.Zero);
        return elapsed.TotalMinutes < 1 ? "Now"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes} min ago"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours} hr ago"
            : elapsed.TotalDays < 2 ? "1 day ago"
            : $"{(int)elapsed.TotalDays} days ago";
    }

    private sealed record RecentItem(string Feature, PortalActivity Activity);
}
