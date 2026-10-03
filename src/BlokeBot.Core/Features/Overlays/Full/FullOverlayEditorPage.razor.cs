using System.Collections.Immutable;
using BlokeBot.Core.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    [Parameter]
    public Guid OverlayId { get; set; }

    [Inject]
    private FullOverlayService _documents { get; set; } = default!;

    [Inject]
    private FullOverlayWidgetRegistry _registry { get; set; } = default!;

    [Inject]
    private FullOverlayDelivery _delivery { get; set; } = default!;

    [Inject]
    private OverlayCueService _media { get; set; } = default!;

    [Inject]
    private IJSRuntime _js { get; set; } = default!;

    [Inject]
    private NavigationManager _navigation { get; set; } = default!;
    private readonly CancellationTokenSource _lifetime = new();
    private ElementReference _root;
    private ElementReference _managementDialog;
    private ElementReference _deleteDialog;
    private IJSObjectReference? _module;
    private IJSObjectReference? _client;
    private DotNetObjectReference<FullOverlayEditorPage>? _reference;
    private FullOverlayView? _row;
    private FullOverlayEditorView _view = FullOverlayEditorView.Empty;
    private ImmutableArray<FullOverlayDiagnostic> _publicationDiagnostics = [];
    private string _name = "Full overlay";
    private string _message = "";
    private string _pane = "preview";
    private bool _loading = true;
    private bool _busy;
    private bool _fullscreen;
    private bool _disposed;
    private bool _initializing;
    private bool _paletteOpen;
    private string _sourceTab = "html";
    private static readonly IReadOnlyList<SegmentedTabItem> _sourceTabs =
    [
        new("html", "HTML"),
        new("css", "CSS"),
    ];

    private void SelectPane(string pane)
    {
        _pane = pane;
        if (pane is "html" or "css")
        {
            _sourceTab = pane;
        }
    }

    private Task SourceTabChanged(string tab)
    {
        _sourceTab = tab;
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        var result = await _documents.GetAsync(PageContext.Session, OverlayId, _lifetime.Token);
        _ = result.Match(
            found =>
            {
                _row = found.Value;
                _name = _row.Name;
                _savedName = _row.Name;
                return true;
            },
            rejected =>
            {
                _message = FullOverlayEditorMessages.Rejection(rejected.Reason);
                return false;
            }
        );
        _loading = false;
        await LoadMediaAsync();
        await LoadBindingsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_row is null || _client is not null || _initializing || _disposed)
        {
            return;
        }

        _initializing = true;
        _module = await _js.InvokeAsync<IJSObjectReference>(
            "import",
            "/Features/Overlays/Full/Editor/EditorClient.js"
        );
        if (_disposed)
        {
            await _module.DisposeAsync();
            return;
        }
        _reference = DotNetObjectReference.Create(this);
        _client = await _module.InvokeAsync<IJSObjectReference>(
            "createClient",
            _root,
            _row.Draft,
            _reference
        );
        if (_disposed)
        {
            await _client.InvokeVoidAsync("dispose");
            return;
        }
        await RefreshPreviewAsync(0);
    }

    [JSInvokable]
    public Task EditorChangedAsync(FullOverlayEditorView view)
    {
        if (_disposed || view.Revision < _view.Revision)
        {
            return Task.CompletedTask;
        }

        var selected = _view.Selected;
        var priorConfiguration = _view.Widget?.Configuration.GetRawText() ?? "";
        _view = view;
        if (selected != view.Selected || _configuration == priorConfiguration)
        {
            _configuration = view.Widget?.Configuration.GetRawText() ?? "";
        }

        return InvokeAsync(StateHasChanged);
    }

    private Task CommandAsync(object command) =>
        CommandAtAsync(command, _view.Selected, _view.Revision);

    private async Task CommandAtAsync(object command, string? selection, long revision)
    {
        if (_client is not null)
        {
            await _client.InvokeVoidAsync("command", command, selection, revision);
        }
    }

    private Task SelectAsync(string key) => CommandAsync(new { kind = "select", key });

    private Task HistoryAsync(string direction) =>
        _client is null
            ? Task.CompletedTask
            : _client.InvokeVoidAsync("history", direction).AsTask();

    private async Task FindAsync(string? selection)
    {
        if (_view.Selected != selection)
        {
            return;
        }
        _sourceTab = "html";
        _pane = "html";
        await InvokeAsync(StateHasChanged);
        if (_client is not null)
        {
            await _client.InvokeVoidAsync("find", selection);
        }
    }

    private Task AddAsync(FullOverlayWidgetKind kind) =>
        _registry.Create(kind, OverlayId) is { } widget
            ? CommandAsync(new { kind = "add", widget })
            : Task.CompletedTask;

    private Task AlignAsync((string Axis, int Edge) value, string? selection, long revision) =>
        _client is null
            ? Task.CompletedTask
            : _client
                .InvokeVoidAsync("align", value.Axis, value.Edge, selection, revision)
                .AsTask();

    private Task ZoomAsync(ChangeEventArgs args) =>
        _client is null
            ? Task.CompletedTask
            : _client
                .InvokeVoidAsync(
                    "zoom",
                    double.Parse(
                        args.Value?.ToString() ?? "0",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
                .AsTask();

    private string FocusLabel() =>
        _view.Focus switch
        {
            "html" => "HTML history",
            "css" => "CSS history",
            _ => "Visual history",
        };

    [JSInvokable]
    public Task BrowserFullscreenChangedAsync(bool active)
    {
        _fullscreen = active;
        return InvokeAsync(StateHasChanged);
    }

    private async Task FullscreenAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            await _client.InvokeVoidAsync("fullscreen");
        }
        catch (JSException)
        {
            _message =
                "The browser could not enter full screen. The editor still uses the available window.";
        }
    }

    private async Task BeforeNavigationAsync(LocationChangingContext context)
    {
        if (
            _hasChanges
            && !await _js.InvokeAsync<bool>(
                "confirm",
                "Leave this overlay and discard unsaved changes?"
            )
        )
        {
            context.PreventNavigation();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        await ReleasePreviewAsync();
        try
        {
            if (_client is not null)
            {
                await _client.InvokeVoidAsync("dispose");
                await _client.DisposeAsync();
            }
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        _reference?.Dispose();
        _lifetime.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
