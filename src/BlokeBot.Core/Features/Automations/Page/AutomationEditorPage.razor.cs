using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Core.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

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
    private AutomationEditorState? _editor;
    private AutomationNodeId? _selectedNodeId;
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
    private string? _feedback;

    private AutomationEditorNode? _selectedNode =>
        _editor?.Nodes.FirstOrDefault(node => node.Id == _selectedNodeId);

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
                ? "Test this flow without contacting Twitch or running live actions."
                : RecentRunDescription(_recentRuns[0])
            : string.Join(
                " → ",
                _sampleOutcomes.Select(outcome =>
                    _editor
                        ?.Nodes.FirstOrDefault(node => node.Id == outcome.NodeId)
                        ?.Definition.Display.Name
                    ?? "Unknown node"
                )
            ) + " · No live action was sent";

    private string _runIconClass =>
        _runTitle.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? "automation-run-icon automation-run-icon--failed"
            : "automation-run-icon";

    private string _runIcon =>
        _runTitle.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "!" : "✓";

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.CustomCommandsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

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

    private async Task LoadCoreAsync(AutomationFlowId? preferredFlowId = null)
    {
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
            RestoreEditor(selected);
            await ValidateCoreAsync(showFeedback: false);
        }
        else
        {
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

    private void NewFlow()
    {
        ResetTransientState();
        _editor = AutomationEditorState.Create("New automation");
        _selectedNodeId = null;
        _nodeLibraryOpen = true;
        _hasChanges = true;
    }

    private async Task SelectFlow(AutomationFlowSnapshot snapshot)
    {
        RestoreEditor(snapshot);
        await ValidateCoreAsync(showFeedback: false);
    }

    private void RestoreEditor(AutomationFlowSnapshot snapshot)
    {
        _editor = AutomationEditorState.Restore(
            snapshot,
            _definitions.ToDictionary(static definition => definition.Id)
        );
        _selectedNodeId =
            _editor
                .Nodes.FirstOrDefault(static node =>
                    node.Definition.Kind == AutomationNodeKind.Action
                )
                ?.Id
            ?? _editor.Nodes.FirstOrDefault()?.Id;
        ResetTransientState();
        _hasChanges = false;
    }

    private void AddNode(AutomationDefinitionDescriptor definition)
    {
        if (_editor is null || CannotAdd(definition))
        {
            return;
        }

        var node = _editor.AddNode(definition);
        ApplyFirstReferenceDefaults(node);
        _selectedNodeId = node.Id;
        _nodeLibraryOpen = false;
        EditorChanged();
    }

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

        _editor.Edges.Add(
            new(
                Guid.NewGuid(),
                request.SourceNodeId,
                request.SourcePortId,
                request.TargetNodeId,
                request.TargetPortId
            )
        );
        EditorChanged();
    }

    private void DeleteEdge(Guid edgeId)
    {
        _ = _editor?.Edges.RemoveAll(edge => edge.Id == edgeId);
        EditorChanged();
    }

    private void DeleteNode(AutomationNodeId nodeId)
    {
        _editor?.RemoveNode(nodeId);
        if (_selectedNodeId == nodeId)
        {
            _selectedNodeId = _editor?.Nodes.FirstOrDefault()?.Id;
        }
        EditorChanged();
    }

    private void MoveNode(AutomationNodeMoveRequest request)
    {
        var node = _editor?.Nodes.FirstOrDefault(candidate => candidate.Id == request.NodeId);
        if (node is null)
        {
            return;
        }

        node.Position = new(new(request.X), new(request.Y));
        EditorChanged();
    }

    private async Task SaveAsync()
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
                    var outcome = await _flowsService.SaveAsync(
                        _editor.Draft(new(requestedHostId)),
                        CancellationToken.None
                    );
                    switch (outcome)
                    {
                        case AutomationFlowSaveOutcome.Saved saved:
                            await LoadCoreAsync(saved.FlowId);
                            _feedback = "Flow saved.";
                            _operationFailed = false;
                            _hasChanges = false;
                            break;
                        case AutomationFlowSaveOutcome.Invalid invalid:
                            ShowValidation(invalid.Errors, "Fix the graph issues before saving.");
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
                                _feedback = "This flow is ready to enable.";
                                _operationFailed = false;
                            }
                            break;
                        case AutomationFlowValidationOutcome.Invalid invalid:
                            ShowValidation(invalid.Errors, "Review the highlighted graph issues.");
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
                    var outcome = await _flowsService.RunSampleAsync(
                        _editor.Draft(new(requestedHostId)),
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
                            ShowValidation(invalid.Errors, "Fix the graph before testing it.");
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
                            ShowValidation(invalid.Errors, "Invalid flows cannot be enabled.");
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

    private async Task DuplicateAsync()
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
                        _feedback = "Flow duplicated as a disabled draft.";
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
            _feedback = "Delete this flow and its run history? Select Confirm delete to continue.";
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

    private void ShowValidation(ImmutableArray<AutomationGraphError> errors, string feedback)
    {
        _validationErrors = errors;
        _validated = true;
        _feedback = feedback;
        _operationFailed = true;
        _selectedNodeId ??= errors.FirstOrDefault(error => error.NodeId is not null)?.NodeId;
    }

    private void ShowUnavailable()
    {
        _feedback =
            "Automations are unavailable for this channel. Check Channel setup and try again.";
        _operationFailed = true;
    }

    private void EditorChanged()
    {
        _validated = false;
        _validationErrors = [];
        _sampleOutcomes = [];
        _feedback = null;
        _operationFailed = false;
        _enableConfirmation = false;
        _deleteConfirmation = false;
        _hasChanges = true;
    }

    private void ResetTransientState()
    {
        _validated = false;
        _validationErrors = [];
        _sampleOutcomes = [];
        _feedback = null;
        _operationFailed = false;
        _nodeLibraryOpen = false;
        _enableConfirmation = false;
        _deleteConfirmation = false;
        _hasChanges = false;
    }

    private bool CannotAdd(AutomationDefinitionDescriptor definition) =>
        definition.Kind == AutomationNodeKind.Source
        && _editor?.Nodes.Any(static node => node.Definition.Kind == AutomationNodeKind.Source)
            == true;

    private AutomationActionCapabilities CurrentCapabilities() =>
        _editor?.Nodes.Aggregate(
            AutomationActionCapabilities.None,
            static (capabilities, node) => capabilities | node.Definition.Capabilities
        ) ?? AutomationActionCapabilities.None;

    private void SelectNode(AutomationNodeId nodeId) => _selectedNodeId = nodeId;

    private void ClearSelection() => _selectedNodeId = null;

    private void SetMode(AutomationEditorMode mode) => _mode = mode;

    private void ToggleNodeLibrary() => _nodeLibraryOpen = !_nodeLibraryOpen;

    private void CancelEnable() => _enableConfirmation = false;

    private void ShowRunDetails() =>
        _feedback = _recentRuns.FirstOrDefault() is { } run
            ? $"Recent run: {RecentRunDescription(run)}"
            : "No persisted runs are available for this channel.";

    private void OpenHelp() =>
        _feedback = "Use the Page help (?) button in the top bar for the complete guide.";

    private string FlowStatusLabel(AutomationFlowSnapshot flow) =>
        flow.Draft.IsEnabled ? "Enabled"
        : flow.Draft.Id == _editor?.Id && _validated && _validationErrors.IsEmpty ? "Ready"
        : "Draft";

    private string FlowStatusDotClass(AutomationFlowSnapshot flow) =>
        FlowStatusLabel(flow) == "Draft"
            ? "automation-status-dot automation-status-dot--draft"
            : "automation-status-dot";

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
            AutomationFlowRunState.Waiting => "Live run is waiting",
            AutomationFlowRunState.Running => "Live run is running",
            _ => "Last live run was stopped",
        };

    private string RecentRunDescription(AutomationRunSummary run)
    {
        var failed = run.Nodes.FirstOrDefault(node => node.State == AutomationNodeRunState.Failed);
        var failedName = failed is null
            ? null
            : _editor
                ?.Nodes.FirstOrDefault(node => node.Id == failed.NodeId)
                ?.Definition.Display.Name;
        return failedName is null
            ? $"{run.Nodes.Length} node outcomes · {FormatTimestamp(run.StartedAtUtc)}"
            : $"Failed at {failedName} · {run.Nodes.Length} node outcomes · {FormatTimestamp(run.StartedAtUtc)}";
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private enum AutomationEditorMode
    {
        Grid,
        List,
    }
}
