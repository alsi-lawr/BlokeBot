namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private async Task SaveAsync()
    {
        if (_sourceFlowId is not null)
        {
            _ = await SaveSourceCoreAsync();
            return;
        }
        if (_editor?.Subflow is not null)
        {
            await ReviewSubflowAsync();
        }
        else
        {
            _ = await SaveCoreAsync();
        }
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (_editor is null || HostId == 0)
        {
            return false;
        }

        _busy = true;
        var succeeded = false;
        var requestedHostId = HostId;
        var requestedEditor = _editor;
        var requestedRevision = _draftRevision;
        var candidate = requestedEditor.Draft(new(requestedHostId));
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.SaveAsync(candidate, CancellationToken.None);
                    if (
                        HostId != requestedHostId
                        || !ReferenceEquals(_editor, requestedEditor)
                        || _draftRevision != requestedRevision
                    )
                    {
                        return;
                    }

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

        if (_editor.Subflow is not null)
        {
            await ReviewSubflowAsync();
            return;
        }
        _busy = true;
        var requestedHostId = HostId;
        var requestedEditor = _editor;
        var requestedRevision = _draftRevision;
        var candidate = requestedEditor.Draft(new(requestedHostId));
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _flowsService.ValidateDraftAsync(
                        candidate,
                        CancellationToken.None
                    );
                    if (
                        HostId != requestedHostId
                        || !ReferenceEquals(_editor, requestedEditor)
                        || _draftRevision != requestedRevision
                    )
                    {
                        return;
                    }

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

    private async Task RunSavedFlowAsync()
    {
        if (_busy || _hasChanges || _editor?.Id is not { } id || !_editor.IsEnabled)
        {
            return;
        }
        var requestedHostId = HostId;
        _busy = true;
        try
        {
            await RunSelectedHostMutationAsync(
                requestedHostId,
                async () =>
                {
                    var outcome = await _manualRuns.RunAsync(
                        new(requestedHostId),
                        id,
                        CancellationToken.None
                    );
                    await LoadCoreAsync(id, preserveViewport: true, preserveHistory: true);
                    _feedback = outcome
                        is AutomationManualRunOutcome.Dispatched
                        {
                            Dispatch.Status: AutomationDispatchStatus.Accepted
                        }
                        ? "Saved flow run admitted with real effects."
                        : "The saved flow could not run. Check its enabled state, manual source and required features.";
                    _operationFailed =
                        outcome
                            is not AutomationManualRunOutcome.Dispatched
                            {
                                Dispatch.Status: AutomationDispatchStatus.Accepted
                            };
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
        await OpenScenariosAsync();
        await RunScenarioAsync();
    }

    private AutomationFlowEnableReview? _enableReview;

    private async Task ToggleEnabledAsync()
    {
        if (_busy || _hasChanges || _editor?.Id is not { } flowId || HostId == 0)
        {
            return;
        }
        var editor = _editor;
        var version = _draftRevision;
        var enabling = !editor.IsEnabled;
        var reviewed = _enableConfirmation ? _enableReview : null;
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
                        CancellationToken.None,
                        reviewed
                    );
                    if (
                        requestedHostId != HostId
                        || !ReferenceEquals(editor, _editor)
                        || version != _draftRevision
                    )
                    {
                        return;
                    }
                    if (
                        outcome is AutomationFlowEnableOutcome.Review initial
                        && reviewed is null
                        && initial.Reviewed.Effects.IsEmpty
                    )
                    {
                        outcome = await _flowsService.SetEnabledAsync(
                            new(requestedHostId),
                            flowId,
                            true,
                            CancellationToken.None,
                            initial.Reviewed
                        );
                    }
                    if (
                        requestedHostId != HostId
                        || !ReferenceEquals(editor, _editor)
                        || version != _draftRevision
                    )
                    {
                        return;
                    }
                    switch (outcome)
                    {
                        case AutomationFlowEnableOutcome.Updated:
                            _enableConfirmation = false;
                            _enableReview = null;
                            await LoadCoreAsync(flowId);
                            _feedback = enabling
                                ? "New starts allowed."
                                : "New starts stopped. Admitted work continues.";
                            _operationFailed = false;
                            break;
                        case AutomationFlowEnableOutcome.Review review:
                            _enableReview = review.Reviewed;
                            _enableConfirmation = true;
                            if (reviewed is not null)
                            {
                                _feedback = "Effects changed. Review them before enabling.";
                            }
                            break;
                        case AutomationFlowEnableOutcome.Invalid invalid:
                            _enableConfirmation = false;
                            _enableReview = null;
                            ShowValidation(
                                invalid.Errors,
                                "Correct the flow before you enable it."
                            );
                            break;
                        default:
                            _enableConfirmation = false;
                            _enableReview = null;
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
}
