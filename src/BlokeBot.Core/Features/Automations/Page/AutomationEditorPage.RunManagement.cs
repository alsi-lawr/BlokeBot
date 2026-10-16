using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private ImmutableArray<AutomationRunSummary> _channelRuns = [];
    private bool _runManagementOpen;
    private bool _runManagementBusy;
    private long _runManagementLoad;
    private AutomationFlowId? _runScopeFlowId;
    private AutomationRunId? _managedRunId;
    private RunCancellationConfirmation? _runCancellationConfirmation;
    private ElementReference _bulkRunButton;
    private ElementReference _confirmRunButton;
    private bool _focusRunConfirmation;

    private sealed record RunCancellationConfirmation(
        int HostId,
        AutomationFlowId? FlowId,
        string Name
    );

    private ImmutableArray<AutomationRunSummary> _managedRuns =>
        [
            .. _channelRuns
                .Where(run =>
                    run.State is AutomationFlowRunState.Running or AutomationFlowRunState.Waiting
                )
                .Where(run => _runScopeFlowId is null || run.FlowId == _runScopeFlowId),
        ];

    private IEnumerable<AutomationFlowId> _runScopeChoices =>
        _flowSnapshots
            .Select(flow => flow.Draft.Id!.Value)
            .Concat(_channelRuns.Select(run => run.FlowId))
            .Distinct();

    private string RunFlowName(AutomationFlowId flowId) =>
        _flowSnapshots.FirstOrDefault(flow => flow.Draft.Id == flowId)?.Draft.Name
        ?? $"Automation {flowId.Value.ToString("N")[^8..]}";

    private void ResetRunManagement()
    {
        _runManagementLoad++;
        _runManagementOpen = false;
        _runCancellationConfirmation = null;
        _managedRunId = null;
        _runScopeFlowId = null;
        _focusRunConfirmation = false;
    }

    private async Task ShowRunDetailsAsync()
    {
        _runManagementOpen = !_runManagementOpen;
        _runCancellationConfirmation = null;
        if (_runManagementOpen)
        {
            _runScopeFlowId = _sourceFlowId ?? (_editor?.Subflow is null ? _editor?.Id : null);
            await RefreshManagedRunsAsync();
        }
    }

    private async Task RefreshManagedRunsAsync()
    {
        var host = HostId;
        var request = ++_runManagementLoad;
        _runCancellationConfirmation = null;
        var result = await _runsService.ListAsync(new(host), CancellationToken.None);
        if (HostId != host || request != _runManagementLoad)
        {
            return;
        }
        _channelRuns = result is AutomationRunQueryOutcome.Available available
            ? available.Runs
            : [];
        _recentRuns = _channelRuns.Where(run => run.FlowId == _editor?.Id).ToImmutableArray();
        SelectAvailableManagedRun();
    }

    private void ChangeRunScope(ChangeEventArgs args)
    {
        _runScopeFlowId = Guid.TryParse(args.Value?.ToString(), out var id) ? new(id) : null;
        _runCancellationConfirmation = null;
        SelectAvailableManagedRun();
    }

    private void ChangeManagedRun(ChangeEventArgs args)
    {
        _runCancellationConfirmation = null;
        _managedRunId =
            Guid.TryParse(args.Value?.ToString(), out var id)
            && _managedRuns.Any(run => run.Id.Value == id)
                ? new(id)
                : null;
    }

    private void SelectAvailableManagedRun()
    {
        if (!_managedRuns.Any(run => run.Id == _managedRunId))
        {
            _managedRunId = _managedRuns.FirstOrDefault()?.Id;
        }
    }

    private void ConfirmRunCancellation()
    {
        _runCancellationConfirmation = new(
            HostId,
            _runScopeFlowId,
            _runScopeFlowId is { } flow ? RunFlowName(flow) : Host?.DisplayName ?? "this channel"
        );
        _focusRunConfirmation = true;
    }

    private async Task KeepRunsAsync()
    {
        _runCancellationConfirmation = null;
        await _bulkRunButton.FocusAsync();
    }

    private async Task FocusRunManagementAsync()
    {
        if (!_focusRunConfirmation || _runCancellationConfirmation is null)
        {
            return;
        }
        _focusRunConfirmation = false;
        await _confirmRunButton.FocusAsync();
    }

    private async Task CancelManagedRunAsync()
    {
        if (_managedRunId is not { } run || _runManagementBusy)
        {
            return;
        }
        var host = HostId;
        await ApplyRunCancellationAsync(
            host,
            () => _runtimeService.CancelRunAsync(new(host), run, CancellationToken.None)
        );
    }

    private async Task CancelConfirmedRunsAsync()
    {
        if (_runCancellationConfirmation is not { } confirmation || _runManagementBusy)
        {
            return;
        }
        await ApplyRunCancellationAsync(
            confirmation.HostId,
            () =>
                !ReferenceEquals(_runCancellationConfirmation, confirmation) ? Task.FromResult(0)
                : confirmation.FlowId is { } flow
                    ? _runtimeService.CancelFlowRunsAsync(
                        new(confirmation.HostId),
                        flow,
                        CancellationToken.None
                    )
                : _runtimeService.CancelChannelRunsAsync(
                    new(confirmation.HostId),
                    CancellationToken.None
                )
        );
    }

    private async Task ApplyRunCancellationAsync(int host, Func<Task<int>> cancel)
    {
        _runManagementBusy = true;
        try
        {
            await RunSelectedHostMutationAsync(
                host,
                async () =>
                {
                    if (HostId != host)
                    {
                        return;
                    }
                    var changed = await cancel();
                    if (HostId != host)
                    {
                        return;
                    }
                    _feedback =
                        changed == 0
                            ? "No unfinished runs were cancelled."
                            : $"Cancelled {changed} unfinished run(s). History retained; new starts are unchanged.";
                    _operationFailed = false;
                    await RefreshManagedRunsAsync();
                }
            );
        }
        finally
        {
            _runManagementBusy = false;
            if (HostId != host)
            {
                ResetRunManagement();
            }
        }
    }
}
