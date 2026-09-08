namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private sealed record CallContext(
        AutomationEditorState Editor,
        AutomationEditorNode Node,
        int Host,
        long Version
    );

    private CallContext? _callContext;
    private AutomationSubflowRevision? _callTarget;
    private AutomationSubflowLibraryPage _callPage = new([], null);
    private string _callSearch = string.Empty;
    private string? _callMessage;
    private int _callOffset;
    private long _callRequest;
    private bool _callLoading;
    private bool _callChoosing;
    private bool _callPageLoading;

    private bool _callInterfaceChanged =>
        _selectedNode?.Subflow is AutomationSubflowInvocationConfiguration authored
        && _callTarget is { } current
        && current.SubflowId == authored.SubflowId
        && !AutomationSubflowDefinitions.Compatible(authored.Interface, current.Interface);

    private CallContext? CurrentCallContext() =>
        _editor is { } editor
        && _selectedNode is { } node
        && node.Definition.Id.Value == AutomationSubflowDefinitions.Invoke
        && _authoringTask == AutomationAuthoringTask.None
        && !_nodeLibraryOpen
        && _inspectorFocusMode == _mode
            ? new(editor, node, HostId, _draftRevision)
            : null;

    private bool SameCall(CallContext context) =>
        context.Host == HostId
        && ReferenceEquals(context.Editor, _editor)
        && ReferenceEquals(context.Node, _selectedNode);

    private bool CurrentCall(CallContext context, long request) =>
        request == _callRequest && SameCall(context) && context.Version == _draftRevision;

    private void ResetCallSelector()
    {
        _callRequest++;
        _callContext = null;
        _callTarget = null;
        _callPage = new([], null);
        _callSearch = string.Empty;
        _callMessage = null;
        _callOffset = 0;
        _callLoading = false;
        _callPageLoading = false;
        _callChoosing = false;
    }

    private async Task EnsureCallSelectorAsync()
    {
        if (CurrentCallContext() is not { } context)
        {
            if (_callContext is not null)
            {
                ResetCallSelector();
            }
            return;
        }
        if (_callContext is not null && SameCall(_callContext))
        {
            return;
        }
        ResetCallSelector();
        _callContext = context;
        var request = ++_callRequest;
        _callLoading = true;
        var target = context.Node.Subflow is AutomationSubflowInvocationConfiguration call
            ? await _subflowsService.LoadCurrentAsync(
                new(context.Host),
                call.SubflowId,
                CancellationToken.None
            )
            : null;
        if (!CurrentCall(context, request))
        {
            if (request == _callRequest && SameCall(context))
            {
                _callContext = null;
                await InvokeAsync(StateHasChanged);
            }
            return;
        }
        _callLoading = false;
        _callTarget = target;
        if (target is null)
        {
            _callMessage = context.Node.Subflow is null
                ? "Choose a subflow."
                : "Choose an available subflow.";
            await ChooseCallTargetAsync();
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task ChooseCallTargetAsync()
    {
        _callChoosing = true;
        await LoadCallPageAsync(0);
    }

    private async Task SearchCallTargetsAsync(string search)
    {
        _callSearch = search;
        await LoadCallPageAsync(0);
    }

    private async Task LoadCallPageAsync(int offset)
    {
        if (CurrentCallContext() is not { } context)
        {
            return;
        }
        var request = ++_callRequest;
        _callPageLoading = true;
        var page = await _subflowsService.ListAsync(
            new(context.Host),
            new(_callSearch, offset),
            CancellationToken.None
        );
        if (!CurrentCall(context, request))
        {
            if (request == _callRequest)
            {
                _callPageLoading = false;
            }
            return;
        }
        _callPageLoading = false;
        _callOffset = offset;
        _callPage = page;
    }

    private Task UpdateCallInterfaceAsync() =>
        _callInterfaceChanged && _callTarget is { } target
            ? SelectCallTargetAsync(target.SubflowId)
            : Task.CompletedTask;

    private async Task SelectCallTargetAsync(AutomationSubflowId id)
    {
        if (CurrentCallContext() is not { } context)
        {
            return;
        }
        var request = ++_callRequest;
        var target = await _subflowsService.LoadCurrentAsync(
            new(context.Host),
            id,
            CancellationToken.None
        );
        if (!CurrentCall(context, request))
        {
            return;
        }
        if (target is null)
        {
            _callMessage = "Choose an available subflow.";
            return;
        }
        context.Node.ReplaceSubflowDefinition(
            AutomationSubflowDefinitions.Invocation(target),
            _catalogService
        );
        _callTarget = target;
        _callChoosing = false;
        _callMessage = null;
        EditorChanged();
        await ValidateCoreAsync(showFeedback: false);
    }
}
