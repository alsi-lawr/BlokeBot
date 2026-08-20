using System.Collections.Immutable;
using BlokeBot.Core.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private readonly Dictionary<
        AutomationReferenceKind,
        IReadOnlyList<AutomationReferenceChoice>
    > _referenceChoices = [];
    private ImmutableArray<AutomationDefinitionDescriptor> _definitions = [];
    private ImmutableArray<AutomationFlowSnapshot> _flowSnapshots = [];
    private ImmutableArray<AutomationGraphError> _validationErrors = [];
    private ImmutableArray<AutomationSampleNodeOutcome> _sampleOutcomes = [];
    private ImmutableArray<AutomationRunSummary> _recentRuns = [];
    private readonly HashSet<AutomationNodeId> _selectedNodeIds = [];
    private readonly HashSet<AutomationDefinitionId> _unavailableDefinitionIds = [];
    private readonly AutomationEditorHistory _history = new();
    private AutomationEditorState? _editor;
    private AutomationFlowCanvas? _canvas;
    private AutomationFlowList? _list;
    private AutomationToolbox? _toolbox;
    private AutomationWorkspaceToolbar? _workspaceToolbar;
    private AutomationEditorMode? _inspectorFocusMode;
    private AutomationNodeId? _selectedNodeId;
    private AutomationNodeId? _disclosedNodeId;
    private long _disclosureGeneration;
    private Guid? _selectedEdgeId;
    private AutomationEditorMode _mode;
    private bool _loading = true;
    private bool _loadFailed;
    private bool _featureEnabled;
    private bool _busy;
    private bool _validated;
    private bool _operationFailed;
    private bool _nodeLibraryOpen;
    private bool _mobileInspectorOpen;
    private bool _focusInspectorAfterRender;
    private bool _focusToolboxAfterRender;
    private bool _enableConfirmation;
    private bool _deleteConfirmation;
    private bool _hasChanges;
    private bool _feedbackFading;
    private string? _feedback;
    private string? _flowRecoveryMessage;
    private string _canvasViewportKey = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _validationFeedbackCancellation;
    private Func<Task>? _pendingTransition;
    private Func<Task>? _acceptedTransition;
    private bool _dirtyDialogOpen;
    private IJSObjectReference? _pageModule;
    private DotNetObjectReference<AutomationEditorPage>? _pageReference;
    private bool _focusMode;
    private bool _browserFullscreen;
    private bool _flowRailCollapsed;
    private bool _editorToolsCollapsed;
    private bool _runDrawerCollapsed;

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.CustomCommandsChanged],
                InvokeAsync,
                RefreshPageAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                _pageModule = await _js.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./Features/Automations/Page/AutomationEditorPage.razor.js"
                );
                _pageReference = DotNetObjectReference.Create(this);
                await _pageModule.InvokeVoidAsync(
                    "initializeDirtyNavigation",
                    _pageReference,
                    _hasChanges
                );
                await _pageModule.InvokeVoidAsync("initializeFullscreen", _pageReference);
                await _pageModule.InvokeVoidAsync("initializeHistoryKeyboard", _pageReference);
                await _pageModule.InvokeVoidAsync("initializeToolboxKeyboard", _pageReference);
            }
            else if (_pageModule is not null)
            {
                await _pageModule.InvokeVoidAsync("setDirtyNavigation", _hasChanges);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }

        if (_focusToolboxAfterRender && _toolbox is not null)
        {
            _focusToolboxAfterRender = false;
            await _toolbox.FocusSearchAsync();
        }

        if (_focusInspectorAfterRender && _pageModule is not null)
        {
            _focusInspectorAfterRender = false;
            await _pageModule.InvokeVoidAsync("focusInspector");
        }

        if (_acceptedTransition is not { } transition)
        {
            return;
        }

        _acceptedTransition = null;
        await transition();
        await InvokeAsync(StateHasChanged);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelValidationFeedback();
            _history.Clear();
        }

        base.Dispose(disposing);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pageModule is not null)
        {
            try
            {
                await _pageModule.InvokeVoidAsync("disposeDirtyNavigation");
                await _pageModule.InvokeVoidAsync("disposeFullscreen");
                await _pageModule.InvokeVoidAsync("disposeHistoryKeyboard");
                await _pageModule.InvokeVoidAsync("disposeToolboxKeyboard");
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (TaskCanceledException) { }

            try
            {
                await _pageModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }

        _pageReference?.Dispose();
        Dispose();
    }

    private enum AutomationEditorMode
    {
        Grid,
        List,
    }

    private sealed record AutomationConnectionDetails(AutomationEdgeKind Kind);
}
