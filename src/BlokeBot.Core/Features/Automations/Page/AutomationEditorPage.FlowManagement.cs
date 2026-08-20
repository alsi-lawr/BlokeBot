namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
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
}
