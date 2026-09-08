namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private long _extractionQuery;

    private async Task OpenExtractionAsync()
    {
        _extractionId = new(Guid.NewGuid());
        _extractionName = "New subflow";
        await OpenAuthoringAsync(AutomationAuthoringTask.Extraction);
        await PreviewExtractionAsync();
    }

    private async Task PreviewExtractionAsync()
    {
        if (_editor is null)
        {
            return;
        }
        var editor = _editor;
        var request = ++_extractionQuery;
        _extraction = null;
        var host = HostId;
        var revision = _draftRevision;
        var snapshot = AutomationEditorDraftSnapshot.Capture(editor);
        var preview = AutomationExtraction.Preview(
            editor,
            new(host),
            _selectedNodeIds,
            _extractionName,
            _extractionId,
            _catalogService
        );
        if (preview.Subflow is { } draft)
        {
            var graph = editor.Draft(new(host));
            var sourceValidation = editor.Subflow is { } source
                ? await _flowsService.ValidateSubflowAsync(
                    new(source.Id, source.Description, InterfaceFor(editor), graph),
                    CancellationToken.None
                )
                : await _flowsService.ValidateAsync(graph, CancellationToken.None);
            if (sourceValidation.Gate is not null || !sourceValidation.Errors.IsEmpty)
            {
                preview = preview with { Errors = ExtractionErrors(sourceValidation) };
            }
            else
            {
                var result = await _subflowsService.PreviewAsync(draft, CancellationToken.None);
                if (result is AutomationSubflowPreviewOutcome.Invalid invalid)
                {
                    preview = preview with { Errors = invalid.Errors };
                }
                else if (result is AutomationSubflowPreviewOutcome.Ready ready)
                {
                    if (
                        request != _extractionQuery
                        || host != HostId
                        || revision != _draftRevision
                        || !ReferenceEquals(editor, _editor)
                        || !_selectedNodeIds.SetEquals(preview.Selection)
                    )
                    {
                        return;
                    }
                    var replacement = AutomationExtraction.Replace(
                        editor,
                        preview,
                        ready.Candidate,
                        _catalogService
                    );
                    var validation = await _flowsService.ValidateExtractionReplacementAsync(
                        replacement.Draft(new(host)),
                        replacement.Subflow is null ? null : InterfaceFor(replacement),
                        replacement.Subflow?.Id,
                        ready.Candidate,
                        CancellationToken.None
                    );
                    preview = preview with { Errors = ExtractionErrors(validation) };
                }
            }
        }
        if (
            request != _extractionQuery
            || host != HostId
            || revision != _draftRevision
            || !ReferenceEquals(editor, _editor)
            || !_selectedNodeIds.SetEquals(preview.Selection)
        )
        {
            return;
        }
        _extraction = preview;
        _extractionSource = snapshot;
    }

    private static System.Collections.Immutable.ImmutableArray<AutomationGraphError> ExtractionErrors(
        AutomationGraphValidation validation
    ) =>
        validation.Gate is null
            ? validation.Errors
            :
            [
                new(
                    null,
                    "extraction-unavailable",
                    "Automations are unavailable for this channel."
                ),
            ];

    private async Task ApplyExtractionAsync()
    {
        if (
            _busy
            || _editor is null
            || _extraction is not { IsValid: true, Subflow: { } draft } preview
            || !_selectedNodeIds.SetEquals(preview.Selection)
            || _extractionSource?.Matches(_editor) is not true
        )
        {
            return;
        }
        var editor = _editor;
        var version = _draftRevision;
        var host = HostId;
        _busy = true;
        try
        {
            var result = await RunAuthoringMutationAsync(
                host,
                () => _subflowsService.PublishAsync(draft, CancellationToken.None)
            );
            if (result is null)
            {
                return;
            }
            if (host != HostId || version != _draftRevision || !ReferenceEquals(editor, _editor))
            {
                return;
            }
            if (result is AutomationSubflowPublishOutcome.Invalid invalid)
            {
                _extraction = preview with { Errors = invalid.Errors };
                return;
            }
            var published = ((AutomationSubflowPublishOutcome.Published)result).Revision;
            var replacement = AutomationExtraction.Replace(
                editor,
                preview,
                published,
                _catalogService
            );
            var graph = replacement.Draft(new(host));
            var validation = replacement.Subflow is { } subflow
                ? await _flowsService.ValidateSubflowAsync(
                    new(subflow.Id, subflow.Description, InterfaceFor(replacement), graph),
                    CancellationToken.None
                )
                : await _flowsService.ValidateAsync(graph, CancellationToken.None);
            if (host != HostId || version != _draftRevision || !ReferenceEquals(editor, _editor))
            {
                return;
            }
            if (validation.Gate is not null || !validation.Errors.IsEmpty)
            {
                _extraction = preview with { Errors = validation.Errors };
                ShowValidation(
                    validation.Errors,
                    "Caller replacement is not valid. The draft is unchanged."
                );
                return;
            }
            _editor = replacement;
            ClearSelection();
            EditorChanged();
            _authoringTask = AutomationAuthoringTask.None;
            _feedback = "Subflow created; selection replaced.";
        }
        finally
        {
            _busy = false;
        }
    }
}
