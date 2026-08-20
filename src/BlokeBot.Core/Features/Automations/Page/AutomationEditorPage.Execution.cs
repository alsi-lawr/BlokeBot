namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
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
                            await LoadCoreAsync(
                                saved.FlowId,
                                preserveViewport: true,
                                preserveHistory: true
                            );
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
}
