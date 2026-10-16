using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Eventing;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private ImmutableArray<AutomationRunSummary> _channelRuns = [];
    private AutomationRunUsage? _runUsage;
    private string? _unfinishedLimit;
    private string? _waitingLimit;
    private long _runLimitsLoad;
    private readonly List<IDisposable> _runLimitSubscriptions = [];

    [Inject]
    private AutomationUiChanges _runUiChanges { get; set; } = default!;
    private string? _runLimitFeedback;
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
        _runLimitsLoad++;
        _runUsage = null;
        _unfinishedLimit = null;
        _waitingLimit = null;
        _runLimitFeedback = null;
        DisposeRunLimitSubscriptions();
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
        await RefreshRunLimitsAsync(resetInputs: true);
    }

    private async Task ChangeRunScope(ChangeEventArgs args)
    {
        _runScopeFlowId = Guid.TryParse(args.Value?.ToString(), out var id) ? new(id) : null;
        _runCancellationConfirmation = null;
        _runLimitFeedback = null;
        SelectAvailableManagedRun();
        await RefreshRunLimitsAsync(resetInputs: true);
    }

    private async Task RefreshRunLimitsAsync(bool resetInputs)
    {
        var host = HostId;
        var flow = _runScopeFlowId;
        var request = ++_runLimitsLoad;
        var result = await _runsService.ReadLimitsAsync(new(host), flow, CancellationToken.None);
        if (host != HostId || flow != _runScopeFlowId || request != _runLimitsLoad)
        {
            return;
        }
        _runUsage = (result as AutomationRunLimitsOutcome.Available)?.Usage;
        if (resetInputs)
        {
            _unfinishedLimit = _runUsage?.Limits.Unfinished?.ToString(CultureInfo.InvariantCulture);
            _waitingLimit = _runUsage?.Limits.Waiting?.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task SaveRunLimitsAsync()
    {
        if (_runManagementBusy)
        {
            return;
        }
        if (
            !TryRunLimit(_unfinishedLimit, out var unfinished)
            || !TryRunLimit(_waitingLimit, out var waiting)
        )
        {
            _runLimitFeedback = "Use a whole number of one or more, or leave blank for unlimited.";
            return;
        }
        var host = HostId;
        var flow = _runScopeFlowId;
        var request = _runLimitsLoad;
        _runManagementBusy = true;
        try
        {
            await RunSelectedHostMutationAsync(
                host,
                async () =>
                {
                    if (HostId != host || _runScopeFlowId != flow || request != _runLimitsLoad)
                    {
                        return;
                    }
                    var result = await _runsService.SaveLimitsAsync(
                        new(host),
                        flow,
                        new(unfinished, waiting),
                        CancellationToken.None
                    );
                    if (HostId != host || _runScopeFlowId != flow || request != _runLimitsLoad)
                    {
                        return;
                    }
                    if (result is AutomationRunLimitsOutcome.Available saved)
                    {
                        _runUsage = saved.Usage;
                        _runLimitFeedback = "Run limits saved. Admitted work continues.";
                    }
                    else
                    {
                        _runLimitFeedback = "Run limits could not be saved for this selection.";
                    }
                }
            );
        }
        finally
        {
            _runManagementBusy = false;
        }
    }

    private static bool TryRunLimit(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        if (
            !int.TryParse(
                text.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            return false;
        }
        value = parsed;
        return parsed > 0;
    }

    private void DisposeRunLimitSubscriptions()
    {
        foreach (var subscription in _runLimitSubscriptions)
        {
            subscription.Dispose();
        }
        _runLimitSubscriptions.Clear();
    }

    private void SubscribeRunLimitRejections()
    {
        DisposeRunLimitSubscriptions();
        var host = HostId;
        var keys = _runScopeChoices
            .SelectMany(flow =>
                new[]
                {
                    new AutomationUiChange.RunLimitRejected(
                        new(host),
                        flow,
                        AutomationAdmissionRejectionReason.FlowUnfinishedLimit
                    ),
                    new AutomationUiChange.RunLimitRejected(
                        new(host),
                        flow,
                        AutomationAdmissionRejectionReason.FlowWaitingLimit
                    ),
                }
            )
            .Concat([
                new(new(host), null, AutomationAdmissionRejectionReason.ChannelUnfinishedLimit),
                new(new(host), null, AutomationAdmissionRejectionReason.ChannelWaitingLimit),
            ]);
        foreach (var key in keys)
        {
            _runLimitSubscriptions.Add(
                _runUiChanges.Subscribe(
                    key,
                    ObserverIdentity.For(typeof(AutomationEditorPage)),
                    async (_, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        await InvokeAsync(async () =>
                        {
                            if (HostId != host)
                            {
                                return;
                            }
                            _runLimitFeedback = RunLimitRejectionMessage(key.Flow, key.Reason);
                            _feedback = _runLimitFeedback;
                            _operationFailed = true;
                            await RefreshRunLimitsAsync(resetInputs: false);
                            StateHasChanged();
                        });
                    }
                )
            );
        }
    }

    private string RunLimitRejectionMessage(
        AutomationFlowId? flow,
        AutomationAdmissionRejectionReason reason
    )
    {
        var target = flow is { } id ? RunFlowName(id) : "channel";
        var waiting =
            reason
            is AutomationAdmissionRejectionReason.FlowWaitingLimit
                or AutomationAdmissionRejectionReason.ChannelWaitingLimit;
        return $"New start rejected: {target} {(waiting ? "waiting threshold" : "unfinished-run limit")} reached.";
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
