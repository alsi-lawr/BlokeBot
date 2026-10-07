using System.Collections.Immutable;
using BlokeBot.Core.Components;
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
    private EditorWorkspace? _workspace;
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
    private bool _loading = true;
    private bool _busy;
    private bool _disposed;
    private bool _initializing;
    private long _editorNotification;
    private bool _toolboxOpen;
    private string _toolboxFeedback = string.Empty;
    private bool _focusToolboxAfterRender;
    private string? _insertedSelection;
    private long _insertedRevision;
    private long _insertedSelectionVersion;
    private FullOverlayToolbox? _toolbox;
    private EditorToolboxToggle? _toolboxToggle;
    private static readonly IReadOnlyList<SegmentedTabItem> _sourceTabs =
    [
        new("html", "HTML"),
        new("css", "CSS"),
    ];

    private static readonly IReadOnlyList<SegmentedTabItem> _modeTabs =
    [
        new("visual", "Visual"),
        new("source", "HTML/CSS"),
    ];

    private string SelectedLabel() =>
        _view.Layers.FirstOrDefault(layer => layer.Key == _view.Selected)?.Label ?? "";

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
        if (_row is null || _disposed)
        {
            return;
        }
        if (_client is null)
        {
            if (_initializing)
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
                _workspace!.Element,
                _row.Draft,
                _reference
            );
            if (_disposed)
            {
                await _client.InvokeVoidAsync("dispose");
                return;
            }
            await RefreshPreviewAsync(0, false);
        }
        if (_focusToolboxAfterRender && _toolbox is not null)
        {
            _focusToolboxAfterRender = false;
            await _toolbox.FocusSearchAsync();
        }
        if (_insertedSelection is { } selected)
        {
            _insertedSelection = null;
            await _client.InvokeVoidAsync(
                "focusInsertedLayer",
                selected,
                _insertedRevision,
                _insertedSelectionVersion
            );
        }
    }

    [JSInvokable]
    public Task EditorChangedAsync(FullOverlayEditorView view, long notification)
    {
        if (_disposed || notification <= _editorNotification || view.Revision < _view.Revision)
        {
            return Task.CompletedTask;
        }

        _editorNotification = notification;
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
        CommandAtAsync(command, _view.Selected, _view.Revision, _view.SelectionVersion);

    private async Task CommandAtAsync(
        object command,
        string? selection,
        long revision,
        long selectionVersion
    )
    {
        if (_client is not null)
        {
            await _client.InvokeVoidAsync(
                "command",
                command,
                selection,
                revision,
                selectionVersion
            );
        }
    }

    private Task SelectAsync(FullOverlayLayerSelection value) =>
        CommandAsync(
            new
            {
                kind = "select",
                key = value.Key,
                toggle = value.Mode == FullOverlaySelectionMode.Toggle,
            }
        );

    private Task HistoryAsync(string direction) =>
        _client is null
            ? Task.CompletedTask
            : _client.InvokeVoidAsync("history", direction).AsTask();

    private Task FindAsync(string? selection) =>
        _client is null || selection is null
            ? Task.CompletedTask
            : _client.InvokeVoidAsync("find", selection).AsTask();

    private sealed record ToolboxInsertion(
        string? Inserted,
        long Revision,
        string Feedback,
        long SelectionVersion
    );

    private async Task AddAsync(FullOverlayWidgetKind kind)
    {
        if (_registry.Create(kind, OverlayId) is { } widget)
        {
            await InsertAsync(new { kind = "add", widget });
        }
        else
        {
            _toolboxFeedback =
                "This widget is no longer declared. Choose an item from the current Toolbox.";
            _focusToolboxAfterRender = true;
        }
    }

    private async Task InsertAsync(object command)
    {
        if (_client is null || _disposed)
        {
            return;
        }
        var revision = _view.Revision;
        var selection = _view.Selected;
        var selectionVersion = _view.SelectionVersion;
        var result = await _client.InvokeAsync<ToolboxInsertion>(
            "insert",
            command,
            selection,
            revision,
            selectionVersion
        );
        if (_disposed)
        {
            return;
        }
        if (result.Inserted is { } inserted)
        {
            _toolboxOpen = false;
            _insertedSelection = inserted;
            _insertedRevision = result.Revision;
            _insertedSelectionVersion = result.SelectionVersion;
        }
        else
        {
            _toolboxFeedback = result.Feedback;
            _focusToolboxAfterRender = true;
        }
        // The document owner's notification owns the view, including any insertion refusal.
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleToolboxAsync()
    {
        if (_toolboxOpen)
        {
            await CloseToolboxAsync();
            return;
        }
        _toolboxFeedback = string.Empty;
        _toolboxOpen = true;
        _focusToolboxAfterRender = true;
    }

    private async Task CloseToolboxAsync()
    {
        _toolboxOpen = false;
        _focusToolboxAfterRender = false;
        await InvokeAsync(StateHasChanged);
        if (_toolboxToggle is not null)
        {
            await _toolboxToggle.FocusAsync();
        }
    }

    [JSInvokable]
    public Task CloseToolboxForModeAsync()
    {
        _toolboxOpen = false;
        _focusToolboxAfterRender = false;
        return InvokeAsync(StateHasChanged);
    }

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
