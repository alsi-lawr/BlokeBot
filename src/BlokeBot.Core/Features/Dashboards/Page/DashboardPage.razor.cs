using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardPage
{
    [Inject]
    private TimeProvider _clock { get; set; } = default!;

    [Parameter]
    public Guid? Dashboard { get; set; }
    private IReadOnlyList<DashboardSummary> _dashboards = [];
    private DashboardSnapshot? _snapshot;
    private DashboardEditSession? _edit;
    private Guid _choice;
    private string _newName { get; set; } = string.Empty;
    private string? _error;
    private string? _feedback;
    private bool _loading;
    private bool _busy;
    private bool _localDirty { get; set; }
    private bool _visible = true;
    private bool _disposed;
    private long _loadGeneration;
    private long _acknowledged;
    private long _interactionGeneration;
    private long _gestureGeneration;
    private readonly SemaphoreSlim _placementGate = new(1, 1);
    private ElementReference _root;
    private ElementReference _picker;
    private bool _focusPicker;
    private IJSObjectReference? _module;
    private IJSObjectReference? _grid;
    private Task? _gridInitialization;
    private DotNetObjectReference<DashboardPage>? _reference;
    private bool _editing =>
        _navigation
            .ToBaseRelativePath(_navigation.Uri)
            .StartsWith("dashboards/editor", StringComparison.Ordinal);
    private bool _dirty => _localDirty || _edit?.Dirty == true;
    private DashboardDocument _document =>
        _edit?.Document ?? _snapshot?.Document ?? DashboardDocument.Empty;

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        _loading = true;
        _error = null;
        await ObserveRouteLoadAsync(async () =>
        {
            _ = await LoadPageContextAsync();
            if (_disposed || generation != _loadGeneration)
            {
                return;
            }
            var host = new DashboardHostId(HostId);
            await BindRecentViewersAsync(generation, host);
            var list = await _configuration.ListAsync(
                PageContext.Session,
                host,
                CancellationToken.None
            );
            if (_disposed || generation != _loadGeneration)
            {
                return;
            }
            if (list is not DashboardResult<IReadOnlyList<DashboardSummary>>.Succeeded loaded)
            {
                _error = "Dashboard access is unavailable. Choose a channel and try again.";
                return;
            }
            _busy = false;
            _dashboards = loaded.Value;
            _choice = _dashboards.FirstOrDefault()?.Id.Value ?? Guid.Empty;
            _snapshot = null;
            _edit = null;
            _localDirty = false;
            _selected = null;
            await DisposeGridAsync();
            _acknowledged = 0;
            _arrangementChosen = false;
            _arrangementKind = DashboardArrangementKind.Desktop;
            _managementOpen = false;
            _focusManagement = false;
            _toolboxOpen = false;
            _focusSettings = false;
            _focusToolbox = false;
            if (Dashboard is { } id)
            {
                var result = await _configuration.GetAsync(
                    PageContext.Session,
                    host,
                    new(id),
                    CancellationToken.None
                );
                if (_disposed || generation != _loadGeneration)
                {
                    return;
                }
                if (result is DashboardResult<DashboardSnapshot>.Succeeded snapshot)
                {
                    _snapshot = snapshot.Value;
                    if (_editing)
                    {
                        _edit = new(snapshot.Value);
                    }
                }
                else
                {
                    _error = "This dashboard is unavailable. Choose another dashboard.";
                }
            }
        });
        if (RouteLoadFailure is not null)
        {
            _error = "Dashboards could not be loaded.";
        }
        if (generation == _loadGeneration)
        {
            _loading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            _focusPicker
            && !_disposed
            && !_loading
            && _error is null
            && (!_editing || _edit is null)
        )
        {
            _focusPicker = false;
            await _picker.FocusAsync();
        }
        if (_disposed || _loading || _error is not null || (_editing && _edit is null))
        {
            return;
        }
        try
        {
            var generation = _loadGeneration;
            _gridInitialization ??= InitializeGridAsync(generation);
            await _gridInitialization;
            if (_disposed || _loading || generation != _loadGeneration || _grid is null)
            {
                return;
            }
            await _grid.InvokeVoidAsync(
                "sync",
                _arrangement.Placements.Select(w => new
                {
                    id = w.WidgetId.Value.ToString(),
                    placement = w.Placement,
                }),
                _acknowledged,
                _dirty,
                _interactionGeneration
            );
            if (_focusManagement)
            {
                _focusManagement = false;
                await _grid.InvokeVoidAsync("focusManagement");
            }
            if (_focusToolbox && _toolbox is not null)
            {
                _focusToolbox = false;
                await _toolbox.FocusSearchAsync();
            }
            if (_focusSettings)
            {
                _focusSettings = false;
                await _grid.InvokeVoidAsync("focusPanel");
            }
        }
        catch (JSDisconnectedException) { }
    }

    private async Task InitializeGridAsync(long generation)
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>(
            "import",
            "./Features/Dashboards/Page/DashboardPage.razor.js"
        );
        if (_disposed || _loading || generation != _loadGeneration)
        {
            return;
        }
        _reference = DotNetObjectReference.Create(this);
        _grid = await _module.InvokeAsync<IJSObjectReference>(
            "create",
            _root,
            _reference,
            generation,
            _interactionGeneration
        );
    }

    private void OpenChoice()
    {
        if (_choice != Guid.Empty)
        {
            _navigation.NavigateTo($"/dashboards/editor/{_choice}");
        }
    }

    private void SelectControl(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var id))
        {
            _navigation.NavigateTo($"/dashboards/control/{id}");
        }
    }

    private async Task CreateAsync()
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        try
        {
            _ = await LoadPageContextAsync();
            var result = await _configuration.CreateAsync(
                PageContext.Session,
                new(HostId),
                _newName,
                CancellationToken.None
            );
            _ = result.Match(
                saved =>
                {
                    _navigation.NavigateTo($"/dashboards/editor/{saved.Value.Id.Value}");
                    return 0;
                },
                reason =>
                {
                    _error = $"Dashboard not created: {reason}.";
                    return 0;
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private void Rename(string name) => _edit?.Rename(name);

    [JSInvokable]
    public Task GestureStarted(long generation, long interaction)
    {
        if (
            !_disposed
            && !_loading
            && !_busy
            && _editing
            && _edit is not null
            && generation == _loadGeneration
            && interaction == _interactionGeneration
        )
        {
            ++_gestureGeneration;
            return InvokeAsync(StateHasChanged);
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task LocalEditStarted(long generation, long interaction)
    {
        if (
            !_disposed
            && generation == _loadGeneration
            && interaction == _interactionGeneration
            && _editing
            && _edit is not null
        )
        {
            _localDirty = true;
            return InvokeAsync(StateHasChanged);
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task PlacementChanged(
        long generation,
        long interaction,
        string id,
        DashboardPlacementTransport placement,
        bool resize,
        long sequence
    )
    {
        await _placementGate.WaitAsync();
        try
        {
            if (
                _disposed
                || generation != _loadGeneration
                || interaction != _interactionGeneration
                || !_editing
                || _edit is null
                || sequence <= _acknowledged
                || !Guid.TryParse(id, out var widgetId)
            )
            {
                return;
            }
            var gesture = _gestureGeneration;
            var widget = _document.Widgets.FirstOrDefault(w => w.Id.Value == widgetId);
            if (widget is not null)
            {
                var applied = await ChangePlacementAsync(
                    widget,
                    placement.ToPlacement(Placement(widget.Id).Order),
                    resize
                );
                if (
                    !applied
                    && !_disposed
                    && generation == _loadGeneration
                    && interaction == _interactionGeneration
                    && gesture == _gestureGeneration
                )
                {
                    // A refused flush is not permission to Save/reset the existing draft history.
                    await CancelGestureAsync(() => { });
                }
            }
            if (_disposed || generation != _loadGeneration || interaction != _interactionGeneration)
            {
                return;
            }
            _acknowledged = sequence;
            if (gesture == _gestureGeneration)
            {
                _localDirty = false;
            }
            await InvokeAsync(StateHasChanged);
        }
        finally
        {
            _ = _placementGate.Release();
        }
    }

    [JSInvokable]
    public Task GestureCancelled(long generation, long interaction)
    {
        if (_disposed || generation != _loadGeneration || interaction != _interactionGeneration)
        {
            return Task.CompletedTask;
        }
        _localDirty = false;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task VisibilityChanged(long generation, bool visible)
    {
        if (_disposed || generation != _loadGeneration)
        {
            return Task.CompletedTask;
        }
        _visible = visible;
        _viewerRefresh?.SetVisible(visible);
        return InvokeAsync(StateHasChanged);
    }

    private async Task BeforeNavigationAsync(LocationChangingContext context)
    {
        if (!_dirty)
        {
            return;
        }
        if (!await _js.InvokeAsync<bool>("confirm", "Discard unsaved dashboard changes?"))
        {
            context.PreventNavigation();
            return;
        }
        await CancelGestureAsync(() => _edit?.Cancel());
    }

    private async Task DisposeGridAsync()
    {
        if (_gridInitialization is not null)
        {
            await _gridInitialization;
            _gridInitialization = null;
        }
        if (_grid is not null)
        {
            try
            {
                await _grid.InvokeVoidAsync("dispose");
                await _grid.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            _grid = null;
        }
        _reference?.Dispose();
        _reference = null;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        ++_loadGeneration;
        _viewerSubscription?.Dispose();
        if (_viewerRefresh is not null)
        {
            await _viewerRefresh.DisposeAsync();
        }
        await DisposeGridAsync();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        Dispose();
    }

    private static string PlacementStyle(DashboardPlacement p) =>
        $"grid-column:{p.Column + 1} / span {p.Width};grid-row:{p.Row + 1} / span {p.Height};order:{p.Order}";
}

public sealed record DashboardPlacementTransport(int Column, int Row, int Width, int Height)
{
    internal DashboardPlacement ToPlacement(int order) => new(Column, Row, Width, Height, order);
}

public sealed record DashboardPendingPlacement(
    string Id,
    DashboardPlacementTransport Placement,
    bool Resize,
    long Sequence,
    long Interaction
);
