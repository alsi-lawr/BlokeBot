using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private sealed record ScheduleHistoryScope(
        int Host,
        AutomationFlowId? Flow,
        AutomationNodeId Source,
        bool Saved
    );

    private ScheduleHistoryScope? _scheduleHistoryScope;
    private ImmutableArray<AutomationScheduleSkipFact> _scheduleSkips = [];
    private long _scheduleHistoryRequest;
    private bool _scheduleHistoryLoading;
    private bool _scheduleHistoryFailed;

    private ScheduleHistoryScope? CurrentScheduleHistoryScope() =>
        !_loading
        && HostId != 0
        && _selectedNode is { } node
        && node.Definition.Id == AutomationDefinitionIds.ScheduledTimeSource
            ? new(
                HostId,
                _editor?.Id,
                node.Id,
                _flowSnapshots.Any(flow =>
                    flow.Draft.Id == _editor?.Id
                    && flow.Draft.Nodes.Any(saved => saved.Id == node.Id)
                )
            )
            : null;

    private void ResetScheduleHistory()
    {
        _scheduleHistoryRequest++;
        _scheduleHistoryScope = null;
        _scheduleSkips = [];
        _scheduleHistoryLoading = false;
        _scheduleHistoryFailed = false;
    }

    private async Task EnsureScheduleHistoryAsync()
    {
        var scope = CurrentScheduleHistoryScope();
        if (_scheduleHistoryScope == scope)
        {
            return;
        }
        ResetScheduleHistory();
        _scheduleHistoryScope = scope;
        await RefreshScheduleHistoryAsync();
    }

    private async Task RefreshScheduleHistoryAsync()
    {
        if (_scheduleHistoryScope is not { Saved: true, Flow: { } flow } scope)
        {
            return;
        }
        var request = ++_scheduleHistoryRequest;
        _scheduleHistoryLoading = true;
        _scheduleHistoryFailed = false;
        StateHasChanged();
        try
        {
            var facts = await _scheduleSkipsService.ListAsync(
                new(scope.Host),
                flow,
                scope.Source,
                CancellationToken.None
            );
            if (request == _scheduleHistoryRequest && scope == CurrentScheduleHistoryScope())
            {
                _scheduleSkips = facts;
            }
        }
        catch (Exception exception)
        {
            if (request == _scheduleHistoryRequest && scope == CurrentScheduleHistoryScope())
            {
                _scheduleHistoryFailed = true;
                ReportUiFault(nameof(RefreshScheduleHistoryAsync), exception);
            }
        }
        finally
        {
            if (request == _scheduleHistoryRequest && scope == CurrentScheduleHistoryScope())
            {
                _scheduleHistoryLoading = false;
                StateHasChanged();
            }
        }
    }

    private static string SkipLabel(AutomationScheduleSkipReason reason) =>
        reason switch
        {
            AutomationScheduleSkipReason.Disabled => "Automation disabled",
            AutomationScheduleSkipReason.NotReady => "Not ready",
            AutomationScheduleSkipReason.Late => "Observed more than two seconds late",
            AutomationScheduleSkipReason.Offline => "Stream offline",
            AutomationScheduleSkipReason.StreamStatusUnknown => "Stream status unknown",
            AutomationScheduleSkipReason.FeatureDisabled => "Required feature disabled",
            AutomationScheduleSkipReason.NoCurrentMatch => "No current matching source",
            AutomationScheduleSkipReason.SourceReconstruction => "Saved source unavailable",
            AutomationScheduleSkipReason.SourceConfiguration => "Source configuration invalid",
            AutomationScheduleSkipReason.SubflowClosure => "Called subflow unavailable",
            AutomationScheduleSkipReason.GraphValidation => "Flow validation failed",
            AutomationScheduleSkipReason.ExecutionUnavailable => "Execution unavailable",
            AutomationScheduleSkipReason.SubflowReferences => "Subflow reference unavailable",
            AutomationScheduleSkipReason.FlowUnfinishedLimit =>
                "Automation unfinished-run limit reached",
            AutomationScheduleSkipReason.FlowWaitingLimit => "Automation waiting threshold reached",
            AutomationScheduleSkipReason.ChannelUnfinishedLimit =>
                "Channel unfinished-run limit reached",
            AutomationScheduleSkipReason.ChannelWaitingLimit => "Channel waiting threshold reached",
        };

    private static string SkipTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString(
            "dd MMM, HH:mm:ss 'UTC'",
            System.Globalization.CultureInfo.InvariantCulture
        );
}
