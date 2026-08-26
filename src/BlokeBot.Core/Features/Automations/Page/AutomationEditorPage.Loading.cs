using System.Collections.Immutable;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
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
        bool preserveViewport = false,
        bool preserveHistory = false
    )
    {
        if (!preserveHistory)
        {
            _history.Clear();
        }

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
        _catalogRevision = catalog.Revision;
        var runQuery = await _runsService.ListAsync(hostId, CancellationToken.None);
        var hostRuns = runQuery is AutomationRunQueryOutcome.Available runs ? runs.Runs : [];
        _recentRuns = hostRuns;
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
            if (RestoreEditor(selected, preserveHistory))
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

        _recentRuns = hostRuns.Where(run => run.FlowId == _editor?.Id).ToImmutableArray();
    }

    private async Task ObserveCatalogChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _ = await _catalogService.WaitForChangeAsync(_catalogRevision, cancellationToken);
                await InvokeAsync(RefreshCatalogAsync);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RefreshCatalogAsync()
    {
        if (HostId == 0)
        {
            return;
        }
        var catalog = await _catalogService.DiscoverAsync(new(HostId), CancellationToken.None);
        _definitions = catalog.Definitions;
        _catalogRevision = catalog.Revision;
        if (_editor is not null)
        {
            var available = _definitions.ToDictionary(static definition => definition.Id);
            foreach (var node in _editor.Nodes)
            {
                if (
                    available.TryGetValue(node.Definition.Id, out var current)
                    && node.RefreshDefinition(current)
                )
                {
                    _ = _unavailableDefinitionIds.Remove(node.Definition.Id);
                }
                else
                {
                    _ = _unavailableDefinitionIds.Add(node.Definition.Id);
                }
            }
        }
        StateHasChanged();
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
        _history.StartNew(_editor);
        _selectedNodeId = null;
        _selectedNodeIds.Clear();
        _selectedEdgeId = null;
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

    private bool RestoreEditor(AutomationFlowSnapshot snapshot, bool preserveHistory = false)
    {
        _unavailableDefinitionIds.Clear();
        var availableDefinitions = _definitions.ToDictionary(static definition => definition.Id);
        var definitions = new Dictionary<AutomationNodeId, AutomationDefinitionDescriptor>();
        foreach (var node in snapshot.Draft.Nodes)
        {
            var id = new AutomationDefinitionId(node.Definition.TypeId);
            if (!availableDefinitions.TryGetValue(id, out var descriptor))
            {
                if (!_catalogService.TryDescribe(id, out descriptor))
                {
                    _editor = null;
                    _disclosedNodeId = null;
                    _history.Clear();
                    _flowRecoveryMessage =
                        $"The saved flow '{snapshot.Draft.Name}' uses an unavailable node type. Restore the node provider, or delete the flow.";
                    return false;
                }

                _ = _unavailableDefinitionIds.Add(id);
            }

            definitions[node.Id] = _catalogService.ValidatePersistedDefinition(node.Definition)
                is AutomationConfigurationCheck.Valid valid
                ? valid.Definition
                : descriptor;
        }

        _editor = AutomationEditorState.Restore(snapshot, definitions);
        _selectedNodeId = null;
        ResetTransientState();
        SetSingleNodeSelection(null);
        if (preserveHistory)
        {
            _history.ContinueAfterSave(_editor);
        }
        else
        {
            _history.StartLoaded(_editor);
        }
        _hasChanges = false;
        return true;
    }
}
