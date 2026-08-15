using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Core.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.EntityFrameworkCore;
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
    private AutomationEditorState? _editor;
    private AutomationFlowCanvas? _canvas;
    private AutomationFlowList? _list;
    private AutomationEditorMode? _inspectorFocusMode;
    private AutomationNodeId? _selectedNodeId;
    private Guid? _selectedEdgeId;
    private AutomationEditorMode _mode;
    private bool _loading = true;
    private bool _loadFailed;
    private bool _featureEnabled;
    private bool _busy;
    private bool _validated;
    private bool _operationFailed;
    private bool _nodeLibraryOpen;
    private bool _enableConfirmation;
    private bool _deleteConfirmation;
    private bool _hasChanges;
    private bool _feedbackFading;
    private string? _feedback;
    private string? _flowRecoveryMessage;
    private string _nodeSearch = string.Empty;
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

    private AutomationEditorNode? _selectedNode =>
        _selectedNodeIds.Count == 1
            ? _editor?.Nodes.FirstOrDefault(node => node.Id == _selectedNodeIds.Single())
            : null;

    private IEnumerable<AutomationDefinitionDescriptor> _filteredDefinitions
    {
        get
        {
            var definitions =
                _editor?.Nodes.Count == 0
                    ? _definitions.Where(static definition =>
                        definition.Kind == AutomationNodeKind.Source
                    )
                    : _definitions;
            if (string.IsNullOrWhiteSpace(_nodeSearch))
            {
                return definitions;
            }

            var search = _nodeSearch.Trim();
            return definitions.Where(definition =>
                definition.Display.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || definition.Display.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || definition.Display.Description.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }
    }

    private string _flowSubtitle =>
        _editor?.Id is null ? "Not saved · changes stay private"
        : _editor.IsEnabled ? "Enabled · saved changes update the live flow"
        : "Saved · changes stay private until enabled";

    private string _validationLabel =>
        _validated
            ? _validationErrors.IsEmpty
                ? "✓ Ready"
                : $"{_validationErrors.Length} issues"
            : "Draft";

    private string _validationClass =>
        _validated && !_validationErrors.IsEmpty
            ? "automation-validation automation-validation--invalid"
        : _validated ? "automation-validation automation-validation--ready"
        : "automation-validation";

    private string _impactWarning
    {
        get
        {
            var capabilities = CurrentCapabilities();
            var effects = new List<string>();
            if (capabilities.HasFlag(AutomationActionCapabilities.SendsChat))
            {
                effects.Add("send public chat messages");
            }
            if (capabilities.HasFlag(AutomationActionCapabilities.PlaysOverlays))
            {
                effects.Add("play overlay cues");
            }
            if (capabilities.HasFlag(AutomationActionCapabilities.ChangesPoints))
            {
                effects.Add("change channel points");
            }
            if (capabilities.HasFlag(AutomationActionCapabilities.CallsTwitchApi))
            {
                effects.Add("call Twitch");
            }
            return effects.Count == 0
                ? "It will respond to live events when enabled."
                : $"It can {string.Join(", ", effects)} when a live event reaches the connected action.";
        }
    }

    private string _runTitle =>
        _sampleOutcomes.IsEmpty
            ? _recentRuns.IsEmpty
                ? "No runs yet"
                : RecentRunTitle(_recentRuns[0])
            : _sampleOutcomes.Any(static outcome => outcome.State == AutomationNodeRunState.Failed)
                ? "Sample run failed"
                : "Last sample run completed";

    private string _runDescription =>
        _sampleOutcomes.IsEmpty
            ? _recentRuns.IsEmpty
                ? "Test this flow. The sample does not contact Twitch or run live actions."
                : RecentRunDescription(_recentRuns[0])
            : string.Join(
                " → ",
                _sampleOutcomes.Select(outcome =>
                    _editor?.Nodes.FirstOrDefault(node => node.Id == outcome.NodeId)?.EffectiveName
                    ?? "Unknown node"
                )
            ) + " · No live action was sent";

    private string _runIconClass =>
        _runTitle.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? "automation-run-icon automation-run-icon--failed"
            : "automation-run-icon";

    private string _runIcon =>
        _runTitle.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "!" : "✓";

    private string _editorClass
    {
        get
        {
            var classes = new List<string> { "automation-editor" };
            if (!_focusMode)
            {
                return classes[0];
            }

            classes.Add("automation-editor--focus");
            if (_editorToolsCollapsed)
            {
                classes.Add("automation-editor--tools-collapsed");
            }
            if (_runDrawerCollapsed)
            {
                classes.Add("automation-editor--runs-collapsed");
            }
            return string.Join(' ', classes);
        }
    }

    private string _editorBodyClass =>
        _focusMode && _flowRailCollapsed
            ? "automation-editor-body automation-editor-body--flows-collapsed"
            : "automation-editor-body";

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
            }
            else if (_pageModule is not null)
            {
                await _pageModule.InvokeVoidAsync("setDirtyNavigation", _hasChanges);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }

        if (_acceptedTransition is not { } transition)
        {
            return;
        }

        _acceptedTransition = null;
        await transition();
        await InvokeAsync(StateHasChanged);
    }

    private Task RefreshPageAsync() => _hasChanges ? Task.CompletedTask : LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            await LoadCoreAsync();
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadCoreAsync(
        AutomationFlowId? preferredFlowId = null,
        bool preserveViewport = false
    )
    {
        var previousFlowId = _editor?.Id;
        _ = await LoadPageContextAsync();
        ResetTransientState();
        if (HostId == 0)
        {
            _featureEnabled = false;
            return;
        }

        var hostId = new AutomationHostId(HostId);
        var catalog = await _catalogService.DiscoverAsync(hostId, CancellationToken.None);
        _featureEnabled = catalog.Availability == AutomationCatalogAvailability.Enabled;
        _definitions = catalog.Definitions;
        if (!_featureEnabled)
        {
            _flowSnapshots = [];
            _editor = null;
            return;
        }

        var flowQuery = await _flowsService.ListAsync(hostId, CancellationToken.None);
        _flowSnapshots = flowQuery is AutomationFlowQueryOutcome.Available available
            ? available.Flows
            : [];
        await LoadReferenceChoicesAsync();
        var selected = preferredFlowId is { } preferred
            ? _flowSnapshots.FirstOrDefault(flow => flow.Draft.Id == preferred)
            : _flowSnapshots.FirstOrDefault();
        if (selected is not null)
        {
            if (!preserveViewport && selected.Draft.Id != previousFlowId)
            {
                ResetCanvasViewport();
            }
            if (RestoreEditor(selected))
            {
                await ValidateCoreAsync(showFeedback: false);
            }
        }
        else
        {
            if (!preserveViewport && previousFlowId is not null)
            {
                ResetCanvasViewport();
            }
            _editor = null;
        }

        var runQuery = await _runsService.ListAsync(hostId, CancellationToken.None);
        _recentRuns = runQuery is AutomationRunQueryOutcome.Available runs
            ? runs.Runs.Where(run => run.FlowId == _editor?.Id).ToImmutableArray()
            : [];
    }

    private async Task LoadReferenceChoicesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        _referenceChoices[AutomationReferenceKind.CustomCommand] = await db
            .CustomCommands.AsNoTracking()
            .Where(command => command.HostId == HostId)
            .OrderBy(static command => command.Name)
            .Select(static command => new AutomationReferenceChoice(
                command.Id.ToString(CultureInfo.InvariantCulture),
                command.Name
            ))
            .ToArrayAsync();
        _referenceChoices[AutomationReferenceKind.CustomReward] = await db
            .TwitchCustomRewards.AsNoTracking()
            .Where(reward => reward.HostId == HostId)
            .OrderBy(static reward => reward.Title)
            .Select(static reward => new AutomationReferenceChoice(
                reward.ProviderRewardId,
                reward.Title
            ))
            .ToArrayAsync();
        var overlayCatalog = await _overlayCues.QueryCatalogAsync(HostId, CancellationToken.None);
        _referenceChoices[AutomationReferenceKind.OverlayTarget] = overlayCatalog
            .Targets.Select(static target => new AutomationReferenceChoice(
                target.Id.ToString("D"),
                target.Name
            ))
            .ToArray();
        _referenceChoices[AutomationReferenceKind.OverlayCue] = overlayCatalog
            .Cues.Select(static cue => new AutomationReferenceChoice(
                cue.Id.ToString("D"),
                cue.Name
            ))
            .ToArray();
    }

    private void StartNewFlow()
    {
        ResetCanvasViewport();
        ResetTransientState();
        _editor = AutomationEditorState.Create("New automation");
        _selectedNodeId = null;
        _selectedNodeIds.Clear();
        _selectedEdgeId = null;
        _nodeSearch = string.Empty;
        _nodeLibraryOpen = true;
        _hasChanges = true;
    }

    private async Task SelectFlowCoreAsync(AutomationFlowSnapshot snapshot)
    {
        if (snapshot.Draft.Id != _editor?.Id)
        {
            ResetCanvasViewport();
        }
        if (RestoreEditor(snapshot))
        {
            await ValidateCoreAsync(showFeedback: false);
        }
    }

    private Task RequestNewFlowAsync() =>
        RequestTransitionAsync(() =>
        {
            StartNewFlow();
            return Task.CompletedTask;
        });

    private Task RequestSelectFlowAsync(AutomationFlowSnapshot snapshot) =>
        snapshot.Draft.Id == _editor?.Id
            ? Task.CompletedTask
            : RequestTransitionAsync(() => SelectFlowCoreAsync(snapshot));

    private bool RestoreEditor(AutomationFlowSnapshot snapshot)
    {
        _unavailableDefinitionIds.Clear();
        var definitions = _definitions.ToDictionary(static definition => definition.Id);
        foreach (var node in snapshot.Draft.Nodes)
        {
            var id = new AutomationDefinitionId(node.Definition.TypeId);
            if (definitions.ContainsKey(id))
            {
                continue;
            }

            if (!_catalogService.TryDescribe(id, out var descriptor))
            {
                _editor = null;
                _flowRecoveryMessage =
                    $"The saved flow '{snapshot.Draft.Name}' uses an unavailable node type. Restore the node provider, or delete the flow.";
                return false;
            }

            definitions.Add(id, descriptor);
            _ = _unavailableDefinitionIds.Add(id);
        }

        _editor = AutomationEditorState.Restore(snapshot, definitions);
        _selectedNodeId = null;
        ResetTransientState();
        SetSingleNodeSelection(null);
        _hasChanges = false;
        return true;
    }

    private void AddNode(AutomationDefinitionDescriptor definition)
    {
        if (_editor is null || !DefinitionIsAvailable(definition))
        {
            return;
        }

        var node = _editor.AddNode(definition);
        ApplyFirstReferenceDefaults(node);
        _selectedNodeId = node.Id;
        SetSingleNodeSelection(node.Id);
        _nodeLibraryOpen = false;
        _nodeSearch = string.Empty;
        EditorChanged();
    }

    private bool DefinitionIsAvailable(AutomationDefinitionDescriptor definition) =>
        definition.TriggerContextRequirement is not { } requirement
        || _editor?.Nodes.Any(node => requirement.CompatibleSources.Contains(node.Definition.Id))
            == true;

    private string? DefinitionUnavailableReason(AutomationDefinitionDescriptor definition) =>
        DefinitionIsAvailable(definition)
            ? null
            : definition.TriggerContextRequirement?.UnavailableReason;

    private string DefinitionLibraryHelp(AutomationDefinitionDescriptor definition) =>
        DefinitionUnavailableReason(definition) ?? definition.Display.Description;

    private void ApplyFirstReferenceDefaults(AutomationEditorNode node)
    {
        foreach (
            var field in node.Definition.Configuration.Where(static field =>
                field.FieldType is AutomationConfigurationFieldType.Reference
            )
        )
        {
            var reference = (AutomationConfigurationFieldType.Reference)field.FieldType;
            if (
                _referenceChoices.TryGetValue(reference.ReferenceKind, out var choices)
                && choices.FirstOrDefault() is { } first
            )
            {
                node.SetValue(field.Id, first.Value);
            }
        }
    }

    private void RenameFlow(ChangeEventArgs args)
    {
        if (_editor is not null)
        {
            _editor.Name = args.Value?.ToString() ?? string.Empty;
            EditorChanged();
        }
    }

    private void ConnectNodes(AutomationConnectionRequest request)
    {
        if (
            _editor is null
            || !CompatibleConnection(_editor, request)
            || _editor.Edges.Any(edge =>
                edge.SourceNodeId == request.SourceNodeId
                && edge.SourcePortId == request.SourcePortId
                && edge.TargetNodeId == request.TargetNodeId
                && edge.TargetPortId == request.TargetPortId
            )
        )
        {
            return;
        }

        var edge = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Flow,
            request.SourceNodeId,
            request.SourcePortId,
            request.TargetNodeId,
            request.TargetPortId
        );
        _editor.Edges.Add(edge);
        _selectedEdgeId = edge.Id;
        _selectedNodeIds.Clear();
        _selectedNodeId = null;
        EditorChanged();
    }

    private void RejectConnection() =>
        ShowTimedValidationFeedback(
            "Release the connection on one compatible input or node.",
            failed: true
        );

    private static bool CompatibleConnection(
        AutomationEditorState editor,
        AutomationConnectionRequest request
    )
    {
        var source = editor.Nodes.FirstOrDefault(node => node.Id == request.SourceNodeId);
        var target = editor.Nodes.FirstOrDefault(node => node.Id == request.TargetNodeId);
        var output = source?.Definition.Outputs.FirstOrDefault(port =>
            port.Id == request.SourcePortId
        );
        var input = target?.Definition.Inputs.FirstOrDefault(port =>
            port.Id == request.TargetPortId
        );
        return source is not null
            && target is not null
            && source.Id != target.Id
            && output is not null
            && input is not null
            && output.ValueType == AutomationPortValueType.Flow
            && output.ValueType == input.ValueType
            && output.Sensitivity == input.Sensitivity;
    }

    private void DeleteEdge(Guid edgeId)
    {
        _ = _editor?.Edges.RemoveAll(edge => edge.Id == edgeId);
        _selectedEdgeId = null;
        EditorChanged();
    }

    private void DeleteNode(AutomationNodeId nodeId) => DeleteNodes([nodeId]);

    private void DeleteNodes(IReadOnlyList<AutomationNodeId> nodeIds)
    {
        if (_editor is null || nodeIds.Count == 0)
        {
            return;
        }

        foreach (var nodeId in nodeIds)
        {
            _editor.RemoveNode(nodeId);
            _ = _selectedNodeIds.Remove(nodeId);
        }

        _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.Single() : null;
        _selectedEdgeId = null;
        EditorChanged();
    }

    private void MoveNode(AutomationNodeMoveRequest request) => MoveNodes([request]);

    private void MoveNodes(IReadOnlyList<AutomationNodeMoveRequest> requests)
    {
        if (_editor is null)
        {
            return;
        }

        foreach (var request in requests)
        {
            var node = _editor.Nodes.FirstOrDefault(candidate => candidate.Id == request.NodeId);
            if (node is null)
            {
                continue;
            }

            node.Position = new(new(request.X), new(request.Y));
        }

        EditorChanged();
    }

    private void ChangeCanvasSettings(AutomationFlowCanvasSettings settings)
    {
        if (_editor is null)
        {
            return;
        }

        _editor.Canvas = settings;
        EditorChanged();
    }

    private void ChangeCanvasSelection(AutomationCanvasSelectionRequest selection)
    {
        _selectedNodeIds.Clear();
        foreach (var nodeId in selection.NodeIds)
        {
            _ = _selectedNodeIds.Add(nodeId);
        }

        _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.Single() : null;
        _selectedEdgeId = selection.EdgeId;
        _inspectorFocusMode = _selectedNodeId is null ? null : AutomationEditorMode.Grid;
    }

    private async Task SaveAsync() => _ = await SaveCoreAsync();

    private async Task<bool> SaveCoreAsync()
    {
        if (_editor is null || HostId == 0)
        {
            return false;
        }

        _busy = true;
        var succeeded = false;
        var requestedHostId = HostId;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.SaveAsync(
                        _editor.Draft(new(requestedHostId)),
                        CancellationToken.None
                    );
                    switch (outcome)
                    {
                        case AutomationFlowSaveOutcome.Saved saved:
                            await LoadCoreAsync(saved.FlowId, preserveViewport: true);
                            _feedback = "Flow saved.";
                            _operationFailed = false;
                            _hasChanges = false;
                            succeeded = true;
                            break;
                        case AutomationFlowSaveOutcome.Invalid invalid:
                            ShowValidation(invalid.Errors, "Correct the flow before you save it.");
                            break;
                        default:
                            ShowUnavailable();
                            break;
                    }
                }
            );
        }
        finally
        {
            _busy = false;
        }

        return succeeded;
    }

    private Task ValidateAsync() => ValidateCoreAsync(showFeedback: true);

    private async Task ValidateCoreAsync(bool showFeedback)
    {
        if (_editor is null || HostId == 0)
        {
            return;
        }

        _busy = true;
        var requestedHostId = HostId;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.ValidateDraftAsync(
                        _editor.Draft(new(requestedHostId)),
                        CancellationToken.None
                    );
                    switch (outcome)
                    {
                        case AutomationFlowValidationOutcome.Valid:
                            _validated = true;
                            _validationErrors = [];
                            if (showFeedback)
                            {
                                ShowTimedValidationFeedback(
                                    "The flow is valid. You can enable it.",
                                    failed: false
                                );
                            }
                            break;
                        case AutomationFlowValidationOutcome.Invalid invalid:
                            ShowValidation(
                                invalid.Errors,
                                "Correct the highlighted items.",
                                fade: showFeedback
                            );
                            break;
                        default:
                            ShowUnavailable();
                            break;
                    }
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunSampleAsync()
    {
        if (_editor is null || HostId == 0)
        {
            return;
        }

        _busy = true;
        var requestedHostId = HostId;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    if (SampleSourceId() is not { } sourceNodeId)
                    {
                        ShowValidation(
                            [new(null, "source-count", "Add one or more trigger nodes.")],
                            "Correct the flow before you test it."
                        );
                        return;
                    }

                    var outcome = await _flowsService.RunSampleAsync(
                        _editor.Draft(new(requestedHostId)),
                        sourceNodeId,
                        CancellationToken.None
                    );
                    switch (outcome)
                    {
                        case AutomationSampleRunOutcome.Completed completed:
                            _sampleOutcomes = completed.Nodes;
                            _feedback = null;
                            _operationFailed = false;
                            break;
                        case AutomationSampleRunOutcome.Failed failed:
                            _sampleOutcomes = failed.Nodes;
                            _feedback = "The sample stopped at the failed node.";
                            _operationFailed = true;
                            break;
                        case AutomationSampleRunOutcome.Invalid invalid:
                            ShowValidation(invalid.Errors, "Correct the flow before you test it.");
                            break;
                        default:
                            ShowUnavailable();
                            break;
                    }
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ToggleEnabledAsync()
    {
        if (_editor?.Id is not { } flowId || HostId == 0)
        {
            return;
        }

        var enabling = !_editor.IsEnabled;
        if (
            enabling
            && CurrentCapabilities() != AutomationActionCapabilities.None
            && !_enableConfirmation
        )
        {
            _enableConfirmation = true;
            return;
        }

        _busy = true;
        var requestedHostId = HostId;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.SetEnabledAsync(
                        new(requestedHostId),
                        flowId,
                        enabling,
                        CancellationToken.None
                    );
                    switch (outcome)
                    {
                        case AutomationFlowEnableOutcome.Updated:
                            await LoadCoreAsync(flowId);
                            _feedback = enabling ? "Flow enabled." : "Flow disabled.";
                            _operationFailed = false;
                            break;
                        case AutomationFlowEnableOutcome.Invalid invalid:
                            ShowValidation(
                                invalid.Errors,
                                "Correct the flow before you enable it."
                            );
                            break;
                        default:
                            ShowUnavailable();
                            break;
                    }
                }
            );
        }
        finally
        {
            _enableConfirmation = false;
            _busy = false;
        }
    }

    private Task RequestDuplicateAsync() => RequestTransitionAsync(DuplicateCoreAsync);

    private async Task DuplicateCoreAsync()
    {
        if (_editor?.Id is not { } flowId || HostId == 0)
        {
            return;
        }

        _busy = true;
        var requestedHostId = HostId;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.DuplicateAsync(
                        new(requestedHostId),
                        flowId,
                        CancellationToken.None
                    );
                    if (outcome is AutomationFlowDuplicateOutcome.Duplicated duplicated)
                    {
                        await LoadCoreAsync(duplicated.FlowId);
                        _feedback = "BlokeBot copied the flow as a disabled draft.";
                        _operationFailed = false;
                    }
                    else
                    {
                        ShowUnavailable();
                    }
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RequestDelete()
    {
        if (!_deleteConfirmation)
        {
            _deleteConfirmation = true;
            _feedback =
                "This action deletes the flow and its run history. Select Confirm delete to continue.";
            _operationFailed = true;
            return;
        }

        await DeleteAsync();
    }

    private async Task DeleteAsync()
    {
        if (_editor?.Id is not { } flowId || HostId == 0)
        {
            return;
        }

        _busy = true;
        var requestedHostId = HostId;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.DeleteAsync(
                        new(requestedHostId),
                        flowId,
                        CancellationToken.None
                    );
                    if (outcome is AutomationFlowDeleteOutcome.Deleted)
                    {
                        await LoadCoreAsync();
                        _feedback = "Flow deleted.";
                        _operationFailed = false;
                    }
                    else
                    {
                        ShowUnavailable();
                    }
                }
            );
        }
        finally
        {
            _deleteConfirmation = false;
            _busy = false;
        }
    }

    private void ShowValidation(
        ImmutableArray<AutomationGraphError> errors,
        string feedback,
        bool fade = false
    )
    {
        _validationErrors = errors;
        _validated = true;
        if (fade)
        {
            ShowTimedValidationFeedback(feedback, failed: true);
        }
        else
        {
            CancelValidationFeedback();
            _feedback = feedback;
            _operationFailed = true;
        }
        if (_selectedNodeIds.Count == 0)
        {
            SetSingleNodeSelection(
                errors.FirstOrDefault(error => error.NodeId is not null)?.NodeId
            );
        }
    }

    private void ShowTimedValidationFeedback(string message, bool failed)
    {
        CancelValidationFeedback();
        _feedback = message;
        _operationFailed = failed;
        _feedbackFading = false;
        _validationFeedbackCancellation = new();
        _ = FadeValidationFeedbackAsync(_validationFeedbackCancellation.Token);
    }

    private async Task FadeValidationFeedbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(9), cancellationToken);
            _feedbackFading = true;
            await InvokeAsync(StateHasChanged);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            _feedback = null;
            _feedbackFading = false;
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void CancelValidationFeedback()
    {
        _validationFeedbackCancellation?.Cancel();
        _validationFeedbackCancellation?.Dispose();
        _validationFeedbackCancellation = null;
        _feedbackFading = false;
    }

    private void ShowUnavailable()
    {
        _feedback =
            "Automations are unavailable for this channel. Check Channel setup. Then, try again.";
        _operationFailed = true;
    }

    private void EditorChanged()
    {
        CancelValidationFeedback();
        _validated = false;
        _validationErrors = [];
        _sampleOutcomes = [];
        _feedback = null;
        _operationFailed = false;
        _enableConfirmation = false;
        _deleteConfirmation = false;
        _hasChanges = true;
    }

    private async Task RequestTransitionAsync(Func<Task> transition)
    {
        if (!_hasChanges)
        {
            await transition();
            return;
        }

        _pendingTransition = transition;
        _dirtyDialogOpen = true;
    }

    private void HandleInternalNavigation(LocationChangingContext context)
    {
        if (!_hasChanges)
        {
            return;
        }

        context.PreventNavigation();
        var targetLocation = context.TargetLocation;
        _pendingTransition = () =>
        {
            _navigation.NavigateTo(targetLocation);
            return Task.CompletedTask;
        };
        _dirtyDialogOpen = true;
    }

    [JSInvokable]
    public Task RequestFullNavigationAsync(string targetLocation) =>
        InvokeAsync(async () =>
        {
            var target = _navigation.ToAbsoluteUri(targetLocation);
            var current = new Uri(_navigation.Uri);
            if (
                !string.Equals(target.Scheme, current.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(target.Host, current.Host, StringComparison.OrdinalIgnoreCase)
                || target.Port != current.Port
            )
            {
                return;
            }

            await RequestTransitionAsync(() =>
                _pageModule!.InvokeVoidAsync("navigateDocument", target.ToString()).AsTask()
            );
            StateHasChanged();
        });

    private async Task SaveDirtyTransitionAsync()
    {
        var transition = _pendingTransition;
        if (transition is null)
        {
            CancelDirtyTransition();
            return;
        }

        if (!await SaveCoreAsync())
        {
            CancelDirtyTransition();
            return;
        }

        await AcceptDirtyTransitionAsync(transition);
    }

    private async Task DiscardDirtyTransitionAsync()
    {
        var transition = _pendingTransition;
        if (transition is null)
        {
            CancelDirtyTransition();
            return;
        }

        _hasChanges = false;
        await AcceptDirtyTransitionAsync(transition);
    }

    private Task AcceptDirtyTransitionAsync(Func<Task> transition)
    {
        CloseDirtyDialog();
        _acceptedTransition = transition;
        return InvokeAsync(StateHasChanged);
    }

    private void CancelDirtyTransition() => CloseDirtyDialog();

    private void CloseDirtyDialog()
    {
        _dirtyDialogOpen = false;
        _pendingTransition = null;
    }

    private void ResetTransientState()
    {
        CancelValidationFeedback();
        _validated = false;
        _validationErrors = [];
        _sampleOutcomes = [];
        _feedback = null;
        _operationFailed = false;
        _nodeLibraryOpen = false;
        _enableConfirmation = false;
        _deleteConfirmation = false;
        _hasChanges = false;
        _flowRecoveryMessage = null;
    }

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

    private void SetSingleNodeSelection(AutomationNodeId? nodeId)
    {
        _selectedNodeIds.Clear();
        if (nodeId is { } selected)
        {
            _ = _selectedNodeIds.Add(selected);
        }

        _selectedNodeId = nodeId;
        _selectedEdgeId = null;
        _inspectorFocusMode = nodeId is null ? null : _mode;
    }

    private void ClearSelection()
    {
        _selectedNodeIds.Clear();
        _selectedNodeId = null;
        _selectedEdgeId = null;
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

    private void SetMode(AutomationEditorMode mode) => _mode = mode;

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

    private void ToggleNodeLibrary()
    {
        _nodeLibraryOpen = !_nodeLibraryOpen;
        if (_nodeLibraryOpen)
        {
            _nodeSearch = string.Empty;
        }
    }

    private void CloseNodeLibrary() => _nodeLibraryOpen = false;

    private void CancelEnable() => _enableConfirmation = false;

    private void ShowRunDetails() =>
        _feedback = _recentRuns.FirstOrDefault() is { } run
            ? $"Recent run: {RecentRunDescription(run)}"
            : "No persisted runs are available for this channel.";

    private string FlowStatusLabel(AutomationFlowSnapshot flow) =>
        flow.Draft.IsEnabled ? "Enabled"
        : flow.Draft.Id == _editor?.Id && _validated && _validationErrors.IsEmpty ? "Ready"
        : "Draft";

    private string FlowStatusDotClass(AutomationFlowSnapshot flow) =>
        FlowStatusLabel(flow) == "Draft"
            ? "automation-status-dot automation-status-dot--draft"
            : "automation-status-dot";

    private string FlowDisplayName(AutomationFlowSnapshot flow)
    {
        var name =
            _editor is { Id: { } editorId } && flow.Draft.Id == editorId
                ? _editor.Name
                : flow.Draft.Name;
        return string.IsNullOrWhiteSpace(name) ? "Untitled flow" : name;
    }

    private static string ModeToken(AutomationEditorMode mode) =>
        mode == AutomationEditorMode.Grid ? "grid" : "list";

    private static string KindToken(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "source",
            AutomationNodeKind.Control => "control",
            AutomationNodeKind.Action => "action",
            _ => "node",
        };

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "Event",
            AutomationNodeKind.Control => "Control",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelValidationFeedback();
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
}
