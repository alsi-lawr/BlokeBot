using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
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
        _disclosedNodeId = null;
        _mobileInspectorOpen = false;
        _focusInspectorAfterRender = false;
        _enableConfirmation = false;
        _deleteConfirmation = false;
        _hasChanges = false;
        _flowRecoveryMessage = null;
    }
}
