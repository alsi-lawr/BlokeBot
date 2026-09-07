using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private AutomationSubflowPreviewOutcome.Ready? _subflowPreview;
    private long _previewRevision = -1;
    private int _callerOffset;
    private long _callerQuery;
    private ImmutableDictionary<
        AutomationSubflowCaller,
        AutomationSubflowCallerDetails
    > _callerDetails = ImmutableDictionary<
        AutomationSubflowCaller,
        AutomationSubflowCallerDetails
    >.Empty;
    private AutomationExtractionPreview? _extraction;
    private AutomationSubflowId _extractionId;
    private string _extractionName = "New subflow";
    private AutomationEditorDraftSnapshot? _extractionSource;
    private AutomationSubflowInterface _currentInterface => InterfaceFor(_editor);

    private static AutomationSubflowInterface InterfaceFor(AutomationEditorState? editor) =>
        editor
            ?.Nodes.FirstOrDefault(node =>
                node.Definition.Id.Value == AutomationSubflowDefinitions.Entry
            )
            ?.Subflow?.Interface
        ?? new([], []);

    private Task NewSubflowAsync() =>
        RequestTransitionAsync(async () =>
        {
            ResetAuthoring();
            var contract = new AutomationSubflowInterface([], []);
            _editor = AutomationEditorState.Create("New subflow");
            _editor.Subflow = new(new(Guid.NewGuid()), string.Empty);
            var entry = AutomationEditorNode.FromDefinition(
                AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, contract),
                _catalogService,
                new(new(80), new(100)),
                "Entry"
            );
            var exit = AutomationEditorNode.FromDefinition(
                AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Exit, contract),
                _catalogService,
                new(new(560), new(100)),
                "Exit"
            );
            _editor.Nodes.AddRange([entry, exit]);
            _editor.Edges.Add(
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    entry.Id,
                    new("complete"),
                    exit.Id,
                    new("flow")
                )
            );
            _history.StartNew(_editor);
            _hasChanges = true;
            ClearSelection();
            ResetCanvasViewport();
            _libraryKind = AutomationLibraryKind.Subflows;
            await OpenInterfaceAsync();
            await LoadSubflowPageAsync(0);
        });

    private void LoadSubflowEditor(AutomationSubflowRevision revision)
    {
        var restored = RestoreAuthoringGraph(revision.Graph);
        if (restored is null)
        {
            _feedback = "A node provider for this subflow is unavailable.";
            return;
        }
        ResetAuthoring();
        _editor = restored;
        _editor.Subflow = new(revision.SubflowId, revision.Description);
        _history.StartLoaded(_editor);
        _hasChanges = false;
        ClearSelection();
        ResetCanvasViewport();
    }

    private AutomationEditorState? RestoreAuthoringGraph(AutomationFlowDraft draft)
    {
        var definitions = new Dictionary<AutomationNodeId, AutomationDefinitionDescriptor>();
        foreach (var node in draft.Nodes)
        {
            if (
                _catalogService.ValidatePersistedDefinition(node.Definition)
                is not AutomationConfigurationCheck.Valid valid
            )
            {
                return null;
            }
            definitions[node.Id] = valid.Definition;
        }
        return AutomationEditorState.Restore(draft, definitions);
    }

    private void ChangeSubflowDescription(ChangeEventArgs args)
    {
        if (_editor?.Subflow is not { } subflow)
        {
            return;
        }
        _editor.Subflow = subflow with { Description = args.Value?.ToString() ?? string.Empty };
        EditorChanged();
    }

    private void ChangeSubflowInterface(AutomationSubflowInterface contract)
    {
        if (_editor?.Subflow is null)
        {
            return;
        }
        if (!AutomationSubflowDefinitions.ValidInterface(contract))
        {
            _feedback = "Use unique valid port names and typed inputs and outputs.";
            _operationFailed = true;
            return;
        }
        foreach (
            var node in _editor.Nodes.Where(node =>
                node.Definition.Id.Value
                    is AutomationSubflowDefinitions.Entry
                        or AutomationSubflowDefinitions.Exit
            )
        )
        {
            node.ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Boundary(node.Definition.Id.Value, contract),
                _catalogService
            );
        }
        EditorChanged();
    }

    private AutomationSubflowDraft SubflowDraft(AutomationEditorState editor) =>
        new(
            editor.Subflow!.Id,
            editor.Subflow.Description,
            _currentInterface,
            editor.Draft(new(HostId))
        );

    private async Task ReviewSubflowAsync()
    {
        if (_editor?.Subflow is null)
        {
            return;
        }
        var editor = _editor;
        var version = _draftRevision;
        var host = HostId;
        _busy = true;
        try
        {
            var preview = await _subflowsService.PreviewAsync(
                SubflowDraft(editor),
                CancellationToken.None
            );
            var details = preview is AutomationSubflowPreviewOutcome.Ready ready
                ? await _subflowsService.DescribeCallersAsync(
                    new(host),
                    ready.IncompatibleCallers,
                    CancellationToken.None
                )
                : [];
            if (host != HostId || !ReferenceEquals(editor, _editor) || version != _draftRevision)
            {
                return;
            }
            _callerDetails = details.ToImmutableDictionary(detail => detail.Caller);
            _subflowPreview = preview as AutomationSubflowPreviewOutcome.Ready;
            _previewRevision = version;
            _callerOffset = 0;
            _authoringTask = AutomationAuthoringTask.Interface;
            if (preview is AutomationSubflowPreviewOutcome.Invalid invalid)
            {
                ShowValidation(invalid.Errors, "Correct the subflow before publishing.");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task LoadCallerPageAsync(int offset)
    {
        if (_subflowPreview is not { } preview)
        {
            return;
        }
        var host = HostId;
        var request = ++_callerQuery;
        var details = await _subflowsService.DescribeCallersAsync(
            new(host),
            preview.IncompatibleCallers.Skip(offset),
            CancellationToken.None
        );
        if (host != HostId || request != _callerQuery || !ReferenceEquals(preview, _subflowPreview))
        {
            return;
        }
        _callerDetails = details.ToImmutableDictionary(detail => detail.Caller);
        _callerOffset = offset;
    }

    private async Task PublishSubflowAsync()
    {
        if (
            _busy
            || _editor?.Subflow is null
            || _subflowPreview is null
            || _previewRevision != _draftRevision
        )
        {
            return;
        }
        var editor = _editor;
        var draft = SubflowDraft(editor);
        var version = _draftRevision;
        var host = HostId;
        _busy = true;
        try
        {
            var result = await RunAuthoringMutationAsync(
                host,
                () => _subflowsService.PublishAsync(draft, CancellationToken.None)
            );
            if (result is null)
            {
                return;
            }
            if (host != HostId || !ReferenceEquals(editor, _editor) || version != _draftRevision)
            {
                return;
            }
            if (result is AutomationSubflowPublishOutcome.Published published)
            {
                _history.ContinueAfterSave(editor);
                _hasChanges = false;
                _subflowPreview = null;
                _feedback = "Subflow published.";
            }
            else if (result is AutomationSubflowPublishOutcome.Invalid invalid)
            {
                ShowValidation(invalid.Errors, "Subflow not published.");
            }
        }
        finally
        {
            _busy = false;
        }
    }
}
