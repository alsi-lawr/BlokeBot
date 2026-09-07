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
        Interface,
        Extraction,
    }

    private AutomationAuthoringTask _authoringTask;
    private long _draftRevision;
    private bool _focusAuthoring;
    private bool _traceOpen;
    private bool _revealSelectedTrace;
    private string _traceTitle = "Execution trace";
    private string? _traceMessage;
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
            _ => "Subflow interface",
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
        if (opening && _pageModule is not null)
        {
            await _pageModule.InvokeVoidAsync(
                "rememberAuthoringOpener",
                task switch
                {
                    AutomationAuthoringTask.Scenarios => "[data-automation-test-flow]",
                    AutomationAuthoringTask.Extraction => "[data-automation-extract-selection]",
                    _ => "[data-automation-interface-opener]",
                }
            );
        }
        _authoringTask = task;
        _nodeLibraryOpen = false;
        _inspectorFocusMode = null;
        _focusAuthoring = opening;
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
        _draftRevision++;
        _traceLoad++;
        _extractionQuery++;
        ResetCallSelector();
        _scenarioCancellation?.Cancel();
        _scenario = null;
        _scenarioFields = [];
        _savedScenarios = [];
        _invalidFixtureInputs.Clear();
        _trace = null;
        _tracePresentation = null;
        _selectedTrace = null;
        _traceHeaders = [];
        _traceOpen = false;
        _authoringTask = AutomationAuthoringTask.None;
        _subflowPreview = null;
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
        var outcome = await _tracesService.ReadAsync(new(host), id, CancellationToken.None);
        if (request != _traceLoad || host != HostId || !ReferenceEquals(editor, _editor))
        {
            return;
        }
        _traceOpen = true;
        _trace = (outcome as AutomationTraceReadOutcome.Available)?.Trace;
        _tracePresentation = _trace is null ? null : AutomationTracePresentation.Create(_trace);
        _sampleOutcomes = _tracePresentation?.RootOutcomes ?? [];
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
                row.Entry.Event.Outcome == AutomationTraceOutcome.Failed
                && row.Entry.Event.Node is not null
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
        if (request == _traceLoad && host == HostId && ReferenceEquals(editor, _editor))
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
