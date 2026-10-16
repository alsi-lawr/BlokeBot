using System.Collections.Immutable;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private enum AutomationAuthoringTask
    {
        None,
        Scenarios,
        SubflowReview,
        Extraction,
    }

    private AutomationDiagnosticSite? _diagnosticSource;
    private bool _diagnosticInspectorOpen;
    private bool _restoreDiagnosticOpener;
    private AutomationAuthoringTask _authoringTask;
    private long _draftRevision;
    private bool _focusAuthoring;
    private bool _traceOpen;
    private bool _revealSelectedTrace;
    private string _traceTitle = "Execution trace";
    private string? _traceMessage;
    private AutomationTraceId? _viewedTraceId;
    private AutomationTraceSnapshot? _trace;
    private AutomationTracePresentation? _tracePresentation;
    private AutomationTraceRow? _selectedTrace;
    private ImmutableArray<AutomationTraceSummary> _traceHeaders = [];
    private int _traceOffset;
    private long _traceLoad;
    private string _authoringTitle =>
        _authoringTask switch
        {
            AutomationAuthoringTask.Scenarios => "Scenarios",
            AutomationAuthoringTask.Extraction => "Extract selection",
            _ => _subflowPublished && _subflowPreview is not null ? "Published" : "Subflow review",
        };

    private Task Handle_authoringKeyAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CloseAuthoringAsync() : Task.CompletedTask;

    private string CallerDescription(AutomationSubflowCaller caller) =>
        _callerDetails.GetValueOrDefault(caller) is { } details
            ? $"{details.Name} · {details.NodeName}"
            : $"Unavailable caller · {caller.NodeId.Value}";

    private async Task OpenAuthoringAsync(AutomationAuthoringTask task)
    {
        var opening = _authoringTask == AutomationAuthoringTask.None;
        _authoringTask = task;
        _nodeLibraryOpen = false;
        _inspectorFocusMode = null;
        _focusAuthoring = opening;
        if (opening && _pageModule is not null)
        {
            await _pageModule.InvokeVoidAsync(
                "rememberAuthoringOpener",
                task switch
                {
                    AutomationAuthoringTask.Scenarios => "[data-automation-test-flow]",
                    AutomationAuthoringTask.Extraction => "[data-automation-extract-selection]",
                    _ => "[data-automation-save-flow]",
                }
            );
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task CloseAuthoringAsync()
    {
        _authoringTask = AutomationAuthoringTask.None;
        await InvokeAsync(StateHasChanged);
        if (_pageModule is not null)
        {
            await _pageModule.InvokeVoidAsync("focusAuthoringOpener");
        }
    }

    private void ResetAuthoring()
    {
        _diagnosticSource = null;
        _diagnosticInspectorOpen = false;
        _draftRevision++;
        _traceLoad++;
        _extractionQuery++;
        ResetCallSelector();
        _scenarioCancellation?.Cancel();
        _scenario = null;
        _scenarioFields = [];
        _savedScenarios = [];
        _invalidFixtureInputs.Clear();
        _currentScenarioResult = null;
        _viewedTraceId = null;
        _trace = null;
        _tracePresentation = null;
        _selectedTrace = null;
        _traceHeaders = [];
        _traceOpen = false;
        _authoringTask = AutomationAuthoringTask.None;
        _subflowPreview = null;
        _subflowPublished = false;
        _subflowReviewRequired = false;
        _callerDetails = ImmutableDictionary<
            AutomationSubflowCaller,
            AutomationSubflowCallerDetails
        >.Empty;
        _extraction = null;
    }

    private async Task<T?> RunAuthoringMutationAsync<T>(int host, Func<Task<T>> mutation)
        where T : class
    {
        T? result = null;
        await RunSelectedHostMutationAsync(host, async () => result = await mutation());
        return result;
    }

    private async Task LoadTraceAsync(AutomationTraceId id)
    {
        var editor = _editor;
        var host = HostId;
        var request = ++_traceLoad;
        var version = _draftRevision;
        var outcome = await _tracesService.ReadAsync(new(host), id, CancellationToken.None);
        if (
            request != _traceLoad
            || version != _draftRevision
            || host != HostId
            || !ReferenceEquals(editor, _editor)
        )
        {
            return;
        }
        _traceOpen = true;
        _viewedTraceId = id;
        _trace = (outcome as AutomationTraceReadOutcome.Available)?.Trace;
        _tracePresentation = _trace is null ? null : AutomationTracePresentation.Create(_trace);
        _traceMessage = outcome switch
        {
            AutomationTraceReadOutcome.Expired => "This trace has expired.",
            AutomationTraceReadOutcome.NotFound => "Trace not found.",
            _ => null,
        };
        _traceOffset = 0;
        _selectedTrace = null;
        if (
            _tracePresentation?.Rows.LastOrDefault(row =>
                AutomationTraceOutcomeSummary.ObservesFailure(row.Entry.Event)
            ) is
            { } failure
        )
        {
            await SelectTraceAsync(failure.Entry.Sequence);
        }
        else if (
            _tracePresentation?.Rows.FirstOrDefault(row => row.Entry.Event.Node is not null) is
            { } first
        )
        {
            await SelectTraceAsync(first.Entry.Sequence);
        }
        var headers = await _tracesService.ListAsync(new(host), editor?.Id, CancellationToken.None);
        if (
            request == _traceLoad
            && version == _draftRevision
            && host == HostId
            && ReferenceEquals(editor, _editor)
        )
        {
            _traceHeaders = headers;
        }
    }

    private void CloseTrace()
    {
        _traceLoad++;
        _traceOpen = false;
    }

    private async Task SelectTraceAsync(int sequence)
    {
        _selectedTrace = _tracePresentation?.Find(sequence);
        _diagnosticSource = null;
        _revealSelectedTrace = true;
        _traceOffset = Math.Max(
            0,
            (sequence - 1) / AutomationTracePanel.PageSize * AutomationTracePanel.PageSize
        );
        if (
            _selectedTrace?.RootNodeId is { } node
            && _editor?.Nodes.Any(value => value.Id == node) is true
        )
        {
            SetSingleNodeSelection(node);
            _inspectorFocusMode = null;
            if (_pageModule is not null)
            {
                await _pageModule.InvokeVoidAsync("revealTraceNode", node.Value.ToString());
            }
        }
        else
        {
            ClearSelection();
        }
        await RefreshDiagnosticSourceAsync();
    }

    private static bool HasDiagnosticMember(
        AutomationEditorNode node,
        AutomationDiagnosticSite site
    ) =>
        node.Id == site.NodeId
        && node.Definition.Id == site.DefinitionId
        && site.Member.Match(
            input =>
                node.Definition.Inputs.Any(port =>
                    port.Id == input.PortId && port.BindingFieldId == input.FieldId
                ),
            output => node.TransformOutputs.Any(port => port.PortId == output.PortId),
            _ => true
        );

    private async Task RefreshDiagnosticSourceAsync()
    {
        var selected = _selectedTrace;
        var editor = _editor;
        var host = HostId;
        if (selected?.Entry.Event.Diagnostic?.Origin is not { } site)
        {
            return;
        }
        if (site.Invocation?.SubflowId is not { } subflow)
        {
            if (
                _trace?.FlowId == editor?.Id
                && editor?.Nodes.Any(node => HasDiagnosticMember(node, site)) is true
            )
            {
                _diagnosticSource = site;
            }
            return;
        }
        var revision = await _subflowsService.LoadCurrentAsync(
            new(host),
            new(subflow),
            CancellationToken.None
        );
        if (
            host != HostId
            || !ReferenceEquals(editor, _editor)
            || !ReferenceEquals(selected, _selectedTrace)
        )
        {
            return;
        }
        if (
            revision is not null
            && revision.Id.Value == site.Invocation.RevisionId
            && RestoreAuthoringGraph(revision.Graph)
                ?.Nodes.Any(node => HasDiagnosticMember(node, site))
                is true
        )
        {
            _diagnosticSource = site;
        }
    }

    private async Task ShowDiagnosticSourceAsync()
    {
        if (_diagnosticSource is not { } site)
        {
            return;
        }
        var host = HostId;
        if (site.Invocation?.SubflowId is { } subflow)
        {
            await RequestTransitionAsync(async () =>
            {
                var activationEditor = _editor;
                var activationRevision = _draftRevision;
                var request = ++_librarySelection;
                var revision = await _subflowsService.LoadCurrentAsync(
                    new(host),
                    new(subflow),
                    CancellationToken.None
                );
                if (
                    host != HostId
                    || !ReferenceEquals(activationEditor, _editor)
                    || activationRevision != _draftRevision
                    || request != _librarySelection
                )
                {
                    return;
                }
                if (
                    revision is null
                    || revision.Id.Value != site.Invocation.RevisionId
                    || RestoreAuthoringGraph(revision.Graph)
                        ?.Nodes.Any(node => HasDiagnosticMember(node, site))
                        is not true
                )
                {
                    _diagnosticSource = null;
                    _feedback = "The recorded source revision is unavailable.";
                    return;
                }
                if (LoadSubflowEditor(revision))
                {
                    await FocusDiagnosticSourceAsync(site);
                }
            });
            return;
        }
        if (
            _trace?.FlowId == _editor?.Id
            && _editor?.Nodes.Any(node => HasDiagnosticMember(node, site)) is true
        )
        {
            await FocusDiagnosticSourceAsync(site);
        }
        else
        {
            _diagnosticSource = null;
        }
    }

    private async Task FocusDiagnosticSourceAsync(AutomationDiagnosticSite site)
    {
        if (_pageModule is not null)
        {
            await _pageModule.InvokeVoidAsync("rememberDiagnosticOpener");
        }
        SetSingleNodeSelection(site.NodeId);
        _diagnosticInspectorOpen = true;
        _disclosedNodeId = site.NodeId;
        _mobileInspectorOpen = true;
        _focusInspectorAfterRender = true;
        _validationFocusField = null;
        _validationFocusPort = null;
        _ = site.Member.Match(
            input =>
            {
                _validationFocusField = input.FieldId.Value;
                return true;
            },
            output =>
            {
                _validationFocusPort = output.PortId.Value;
                return true;
            },
            _ => true
        );
        await InvokeAsync(StateHasChanged);
    }

    private async Task SelectTraceRunAsync(string id)
    {
        if (!Guid.TryParse(id, out var value))
        {
            return;
        }
        _traceTitle = _traceHeaders
            .FirstOrDefault(header => header.Id.Value == value)
            ?.ProductionRunId
            is null
            ? "Test execution trace"
            : "Live execution trace";
        await LoadTraceAsync(new(value));
    }

    private async Task OpenRecentTraceAsync()
    {
        var editor = _editor;
        var host = HostId;
        var request = ++_traceLoad;
        var headers = await _tracesService.ListAsync(new(host), editor?.Id, CancellationToken.None);
        if (request != _traceLoad || host != HostId || !ReferenceEquals(editor, _editor))
        {
            return;
        }
        _traceHeaders = headers;
        if (_traceHeaders.FirstOrDefault() is { } first)
        {
            await SelectTraceRunAsync(first.Id.Value.ToString());
        }
        else
        {
            _traceOpen = true;
            _traceMessage = "No traces available.";
        }
    }
}
