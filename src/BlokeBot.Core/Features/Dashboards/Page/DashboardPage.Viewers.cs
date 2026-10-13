using BlokeBot.Eventing;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardPage
{
    [Inject]
    private DashboardRecentViewers _recentViewerReader { get; set; } = default!;
    private DashboardRecentViewerView? _recentViewers;
    private DashboardRefreshQueue? _viewerRefresh;
    private IDisposable? _viewerSubscription;
    private readonly ObserverIdentity _viewerObserver = ObserverIdentity.Named(
        $"DashboardViewers.{Guid.NewGuid():N}"
    );

    private async Task BindRecentViewersAsync(long generation, DashboardHostId host)
    {
        _viewerSubscription?.Dispose();
        if (_viewerRefresh is not null)
        {
            await _viewerRefresh.DisposeAsync();
        }
        _recentViewers = new(host, null);
        if (_editing)
        {
            return;
        }
        var session = PageContext.Session;
        _viewerRefresh = new(
            () =>
                InvokeAsync(async () =>
                {
                    var result = await _recentViewerReader.ReadAsync(
                        session,
                        host,
                        CancellationToken.None
                    );
                    if (_disposed || generation != _loadGeneration || HostId != host.Value)
                    {
                        return;
                    }
                    _recentViewers = new(host, result);
                    StateHasChanged();
                }),
            exception => ReportUiFault("Dashboard recent viewer refresh", exception)
        );
        _viewerSubscription = _recentViewerReader.Subscribe(
            host,
            _viewerObserver,
            (_, _) =>
            {
                if (!_disposed && generation == _loadGeneration && HostId == host.Value)
                {
                    _viewerRefresh.Request();
                }
                return ValueTask.CompletedTask;
            }
        );
        _viewerRefresh.SetVisible(_visible);
    }
}
