using System.Collections.Immutable;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
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
        if (_editor is null || !_history.Record(_editor))
        {
            return;
        }

        DraftChanged();
    }

    private void DraftChanged()
    {
        CancelValidationFeedback();
        _validated = false;
        _validationErrors = [];
        _sampleOutcomes = [];
        _feedback = null;
        _operationFailed = false;
        _enableConfirmation = false;
        _deleteConfirmation = false;
        _hasChanges = _editor is not null && _history.IsDirty(_editor);
    }

    private void Undo()
    {
        if (_editor is not null && _history.Undo(_editor) is { } restored)
        {
            ApplyHistory(restored);
        }
    }

    private void Redo()
    {
        if (_editor is not null && _history.Redo(_editor) is { } restored)
        {
            ApplyHistory(restored);
        }
    }

    private void ApplyHistory(AutomationEditorState restored)
    {
        _editor = restored;
        _disclosedNodeId = null;
        _ = _selectedNodeIds.RemoveWhere(nodeId => restored.Nodes.All(node => node.Id != nodeId));
        _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.Single() : null;
        if (_selectedEdgeId is { } edgeId && restored.Edges.All(edge => edge.Id != edgeId))
        {
            _selectedEdgeId = null;
        }

        DraftChanged();
    }

    [JSInvokable]
    public Task ApplyEditorHistoryShortcutAsync(string action) =>
        InvokeAsync(() =>
        {
            switch (AutomationEditorHistoryShortcut.Parse(action))
            {
                case AutomationEditorHistoryAction.Undo:
                    Undo();
                    break;
                case AutomationEditorHistoryAction.Redo:
                    Redo();
                    break;
            }

            StateHasChanged();
        });
}
