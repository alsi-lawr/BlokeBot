using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private AutomationSubflowLibraryPage _subflowPage = new([], null);
    private string _subflowSearch = string.Empty;
    private int _subflowOffset;
    private long _subflowQuery;
    private bool _showInterface;
    private AutomationSubflowRevision? _libraryRevision;
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

    private async Task OpenSubflowsAsync()
    {
        await OpenAuthoringAsync(AutomationAuthoringTask.Subflows);
        _showInterface = false;
        await LoadSubflowPageAsync(0);
    }

    private async Task LoadSubflowPageAsync(int offset)
    {
        var request = ++_subflowQuery;
        var host = HostId;
        var page = await _subflowsService.ListAsync(
            new(host),
            new(_subflowSearch, offset),
            CancellationToken.None
        );
        if (request != _subflowQuery || host != HostId)
        {
            return;
        }
        _subflowOffset = offset;
        _subflowPage = page;
        _libraryRevision = null;
    }

    private async Task SearchSubflowsAsync(ChangeEventArgs args)
    {
        _subflowSearch = args.Value?.ToString() ?? string.Empty;
        await LoadSubflowPageAsync(0);
    }

    private async Task SelectLibraryRevisionAsync(AutomationSubflowRevisionId id)
    {
        var host = HostId;
        var request = ++_subflowQuery;
        var loaded = await _subflowsService.LoadClosureAsync(
            new(host),
            [id],
            CancellationToken.None
        );
        if (host != HostId || request != _subflowQuery)
        {
            return;
        }
        _libraryRevision = (
            loaded as AutomationSubflowClosureOutcome.Available
        )?.Closure.Revisions.FirstOrDefault(value => value.Id == id);
        if (_libraryRevision is null)
        {
            _feedback = "This subflow revision is unavailable.";
        }
    }

    private Task NewSubflowAsync() =>
        RequestTransitionAsync(() =>
        {
            ResetAuthoring();
            var contract = new AutomationSubflowInterface([], []);
            _editor = AutomationEditorState.Create("New subflow");
            _editor.Subflow = new(new(Guid.NewGuid()), string.Empty, null);
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
            _authoringTask = AutomationAuthoringTask.Subflows;
            _showInterface = true;
            return Task.CompletedTask;
        });

    private Task EditLibraryRevisionAsync() =>
        _libraryRevision is { } revision
            ? RequestTransitionAsync(() =>
            {
                LoadSubflowEditor(revision);
                return Task.CompletedTask;
            })
            : Task.CompletedTask;

    private void LoadSubflowEditor(AutomationSubflowRevision revision)
    {
        var restored = RestoreAuthoringGraph(revision.Graph);
        if (restored is null)
        {
            _feedback = "A node provider for this revision is unavailable.";
            return;
        }
        ResetAuthoring();
        _editor = restored;
        _editor.Subflow = new(revision.SubflowId, revision.Description, revision.Id);
        _history.StartLoaded(_editor);
        _hasChanges = false;
        ClearSelection();
        ResetCanvasViewport();
        _authoringTask = AutomationAuthoringTask.Subflows;
        _showInterface = true;
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
            var fixedInputs = node.Subflow!.FixedInputs.Where(pair =>
                    contract.Outputs.Any(port => port.Id == pair.Key)
                )
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Value);
            node.ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Boundary(
                    node.Definition.Id.Value,
                    contract,
                    fixedInputs
                ),
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
            _authoringTask = AutomationAuthoringTask.Subflows;
            _showInterface = true;
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
                editor.Subflow = editor.Subflow with { BaseRevision = published.Revision.Id };
                _history.ContinueAfterSave(editor);
                _hasChanges = false;
                _subflowPreview = null;
                _feedback = $"Revision {published.Revision.Revision} published.";
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

    private void InsertInvocation()
    {
        if (_editor is null || _libraryRevision is null)
        {
            return;
        }
        var node = AutomationEditorNode.FromDefinition(
            AutomationSubflowDefinitions.Invocation(
                _libraryRevision,
                _libraryRevision.Interface.Inputs.ToImmutableDictionary(
                    port => port.Id,
                    port => AutomationEditorNode.DefaultFixtureValue(port.ValueType)
                )
            ),
            _catalogService,
            new(new(160), new(180)),
            _libraryRevision.Graph.Name
        );
        _editor.Nodes.Add(node);
        SetSingleNodeSelection(node.Id);
        EditorChanged();
    }

    private void RebindInvocation()
    {
        if (
            _selectedNode?.Subflow is not { } previous
            || _libraryRevision is null
            || _selectedNode.Definition.Id.Value != AutomationSubflowDefinitions.Invoke
        )
        {
            return;
        }
        if (
            !AutomationSubflowDefinitions.Compatible(previous.Interface, _libraryRevision.Interface)
        )
        {
            _feedback = "The selected revision has an incompatible interface.";
            _operationFailed = true;
            return;
        }
        _selectedNode.ReplaceSubflowDefinition(
            AutomationSubflowDefinitions.Invocation(
                _libraryRevision,
                previous.FixedInputs.ToImmutableDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Value
                )
            ),
            _catalogService
        );
        EditorChanged();
    }
}
