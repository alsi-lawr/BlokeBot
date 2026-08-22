using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private AutomationActionCapabilities CurrentCapabilities() =>
        _editor?.Nodes.Aggregate(
            AutomationActionCapabilities.None,
            static (capabilities, node) => capabilities | node.Definition.Capabilities
        ) ?? AutomationActionCapabilities.None;

    private AutomationNodeId? SampleSourceId()
    {
        var sources = _editor?.Nodes.Where(static node =>
            node.Definition.Kind == AutomationNodeKind.Source
        );
        return sources?.FirstOrDefault(node => node.Id == _selectedNodeId)?.Id
            ?? sources
                ?.OrderBy(static node => node.Position.Y.Value)
                .ThenBy(static node => node.Position.X.Value)
                .ThenBy(static node => node.Id.Value)
                .FirstOrDefault()
                ?.Id;
    }

    private void SelectNode(AutomationNodeId nodeId) => SetSingleNodeSelection(nodeId);

    private void SetCanvasNodeDisclosure(AutomationCanvasDisclosureRequest request)
    {
        if (request.Generation < _disclosureGeneration)
        {
            return;
        }

        _disclosureGeneration = request.Generation;
        if (
            request.NodeId is not { } nodeId
            || _editor?.Nodes.Any(node => node.Id == nodeId) is not true
        )
        {
            _disclosedNodeId = null;
            return;
        }

        SetSingleNodeSelection(nodeId);
        _disclosedNodeId = nodeId;
    }

    private void SetSingleNodeSelection(AutomationNodeId? nodeId)
    {
        _disclosedNodeId = null;
        _selectedNodeIds.Clear();
        if (nodeId is { } selected)
        {
            _ = _selectedNodeIds.Add(selected);
        }

        _selectedNodeId = nodeId;
        _selectedEdgeId = null;
        _mobileInspectorOpen = false;
        _inspectorFocusMode = nodeId is null ? null : _mode;
    }

    private void ClearSelection()
    {
        _disclosedNodeId = null;
        _selectedNodeIds.Clear();
        _selectedNodeId = null;
        _selectedEdgeId = null;
        _mobileInspectorOpen = false;
        _inspectorFocusMode = null;
    }

    private void CloseInspector()
    {
        if (_selectedNodeId is { } nodeId)
        {
            var focusMode =
                _inspectorFocusMode is { } requestedMode && requestedMode == _mode
                    ? requestedMode
                    : _mode;
            if (focusMode == AutomationEditorMode.List)
            {
                _list?.RestoreFocusAfterRender(nodeId);
            }
            else
            {
                _canvas?.RestoreFocusAfterRender(nodeId);
            }
        }

        ClearSelection();
    }

    private void SetMode(AutomationEditorMode mode)
    {
        if (_mode != mode)
        {
            _disclosedNodeId = null;
            _mode = mode;
        }
    }

    private void ToggleFocusMode() => _focusMode = !_focusMode;

    private void ToggleFlowRail() => _flowRailCollapsed = !_flowRailCollapsed;

    private void ToggleEditorTools() => _editorToolsCollapsed = !_editorToolsCollapsed;

    private void ToggleRunDrawer() => _runDrawerCollapsed = !_runDrawerCollapsed;

    private async Task ToggleBrowserFullscreenAsync()
    {
        if (_pageModule is null)
        {
            _feedback = "Browser full screen did not start. Try again.";
            _operationFailed = true;
            return;
        }

        try
        {
            await _pageModule.InvokeVoidAsync("toggleBrowserFullscreen");
        }
        catch (JSException exception)
        {
            _feedback = "Browser full screen did not start. Try again.";
            _operationFailed = true;
            ReportUiFault(nameof(ToggleBrowserFullscreenAsync), exception);
        }
    }

    [JSInvokable]
    public Task BrowserFullscreenChangedAsync(bool active) =>
        InvokeAsync(() =>
        {
            _browserFullscreen = active;
            StateHasChanged();
        });

    private void ResetCanvasViewport() => _canvasViewportKey = Guid.NewGuid().ToString("N");

    private Task ToggleNodeLibraryAsync() =>
        _nodeLibraryOpen ? CloseNodeLibraryAsync() : OpenNodeLibraryAsync();

    private async Task OpenNodeLibraryAsync()
    {
        _nodeLibraryOpen = true;
        _focusToolboxAfterRender = true;
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OpenToolboxFromShortcutAsync() => OpenNodeLibraryAsync();

    private async Task CloseNodeLibraryAsync()
    {
        _nodeLibraryOpen = false;
        await InvokeAsync(StateHasChanged);
        if (_workspaceToolbar is not null)
        {
            await _workspaceToolbar.FocusToolboxButtonAsync();
        }
    }

    private Task CloseNodeLibraryFromOutsideAsync() =>
        _nodeLibraryOpen ? CloseNodeLibraryAsync() : Task.CompletedTask;

    private async Task FocusInspectorAsync()
    {
        _mobileInspectorOpen = true;
        await InvokeAsync(StateHasChanged);
        if (_pageModule is not null)
        {
            await _pageModule.InvokeVoidAsync("focusInspector");
        }
    }

    private void ChangeOrientation(ChangeEventArgs args)
    {
        if (
            _editor is not null
            && Enum.TryParse<AutomationFlowOrientation>(args.Value?.ToString(), out var orientation)
        )
        {
            ChangeCanvasSettings(_editor.Canvas with { Orientation = orientation });
        }
    }

    private void ChangeEdgeStyle(ChangeEventArgs args)
    {
        if (
            _editor is not null
            && Enum.TryParse<AutomationEdgeStyle>(args.Value?.ToString(), out var edgeStyle)
        )
        {
            ChangeCanvasSettings(_editor.Canvas with { EdgeStyle = edgeStyle });
        }
    }

    private void CancelEnable() => _enableConfirmation = false;

    private void ShowRunDetails() =>
        _feedback = _recentRuns.FirstOrDefault() is { } run
            ? $"Recent run: {RecentRunDescription(run)}"
            : "No persisted runs are available for this channel.";

    private static string ModeToken(AutomationEditorMode mode) =>
        mode == AutomationEditorMode.Grid ? "grid" : "list";

    private static string RecentRunTitle(AutomationRunSummary run) =>
        run.State switch
        {
            AutomationFlowRunState.Completed => "Last live run completed",
            AutomationFlowRunState.Failed => "Last live run failed",
            AutomationFlowRunState.Waiting => "Live run waits",
            AutomationFlowRunState.Running => "Live run is active",
            _ => "Last live run was stopped",
        };

    private string RecentRunDescription(AutomationRunSummary run)
    {
        var failed = run.FailedNode;
        var failedName = failed is null
            ? null
            : _editor?.Nodes.FirstOrDefault(node => node.Id == failed.NodeId)?.EffectiveName;
        return failedName is null
                ? $"{run.Nodes.Length} node outcomes · {FormatTimestamp(run.StartedAtUtc)}"
            : failed!.State == AutomationNodeRunState.ContinuedAfterFailure
                ? $"Failure continued at {failedName} · {run.Nodes.Length} node outcomes · {FormatTimestamp(run.StartedAtUtc)}"
            : $"Failed at {failedName} · {run.Nodes.Length} node outcomes · {FormatTimestamp(run.StartedAtUtc)}";
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
