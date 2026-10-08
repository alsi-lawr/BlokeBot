using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private ImmutableArray<AutomationFlowAuthoringEntry> _authoringEntries = [];
    private AutomationFlowId? _sourceFlowId;
    private string _sourceName = string.Empty;
    private string _sourceProviderStatus = string.Empty;
    private string _sourceText = string.Empty;
    private string _sourceOriginalText = string.Empty;
    private string _sourceStatus = "The original is unchanged until you save a valid repair.";
    private string[] _sourceErrors = [];
    private bool _sourceEnabled;
    private long _sourceGeneration;
    private long _sourceRevision;
    private long _sourceValidatedRevision = -1;
    private long _sourceFocusRequest;
    private bool _sourceValidated => _sourceValidatedRevision == _sourceRevision;

    private void ResetSource()
    {
        _sourceGeneration++;
        _sourceFlowId = null;
        _sourceText = _sourceOriginalText = _sourceName = string.Empty;
        _sourceErrors = [];
        _sourceRevision = 0;
        _sourceValidatedRevision = -1;
        _sourceStatus = "The original is unchanged until you save a valid repair.";
    }

    private Task RequestRepairFlowAsync(AutomationFlowId id) =>
        id == (_editor?.Id ?? _sourceFlowId)
            ? Task.CompletedTask
            : RequestTransitionAsync(() => LoadAuthoringFlowAsync(id));

    private async Task LoadAuthoringFlowAsync(AutomationFlowId id, bool preserveHistory = false)
    {
        var host = HostId;
        var generation = ++_sourceGeneration;
        var outcome = await _flowsService.ReadForAuthoringAsync(
            new(host),
            id,
            CancellationToken.None
        );
        if (HostId != host || _sourceGeneration != generation)
        {
            return;
        }

        await outcome.Match<Task>(
            async editor =>
            {
                if (RestoreEditor(editor.Snapshot, preserveHistory))
                {
                    if (!editor.Errors.IsEmpty)
                    {
                        ShowValidation(
                            editor.Errors,
                            "Correct the highlighted items, then validate and save."
                        );
                    }
                    else
                    {
                        await ValidateCoreAsync(showFeedback: false);
                    }

                    return;
                }
                OpenSource(id, editor.Original, editor.Snapshot.Draft.IsEnabled);
            },
            json =>
            {
                OpenSource(json.Id, json.Original, json.IsEnabled);
                return Task.CompletedTask;
            },
            _ =>
            {
                _editor = null;
                ResetSource();
                ShowUnavailable();
                return Task.CompletedTask;
            }
        );
    }

    private void OpenSource(
        AutomationFlowId id,
        AutomationAuthoredFlowSource original,
        bool enabled
    )
    {
        ResetTransientState();
        _editor = null;
        _history.Clear();
        _sourceFlowId = id;
        _sourceName = original.Name;
        _sourceProviderStatus = original.Nodes.All(node =>
            _definitions.Any(definition => definition.Id.Value == node.DefinitionId)
        )
            ? "Node providers are available. Versions and values are checked during validation."
            : "A required node provider or channel tool is unavailable.";
        _sourceEnabled = enabled;
        _sourceText = _sourceOriginalText = AutomationAuthoredFlowCodec.Write(original);
        _sourceFocusRequest++;
    }

    private Task ChangeSourceAsync(string text)
    {
        _sourceText = text;
        _sourceRevision++;
        _sourceValidatedRevision = -1;
        _sourceErrors = [];
        _hasChanges = text != _sourceOriginalText;
        _sourceStatus = "The original is unchanged until you save a valid repair.";
        return Task.CompletedTask;
    }

    private void ParseSource()
    {
        var parse = AutomationAuthoredFlowCodec.Parse(_sourceText);
        _sourceErrors = parse.Match<string[]>(
            _ => [],
            invalid => invalid.Errors.Select(error => error.Path + ": " + error.Message).ToArray()
        );
        _sourceStatus =
            _sourceErrors.Length == 0
                ? "JSON parsed. Validate the flow before saving."
                : "The candidate needs repair. The original is unchanged.";
        if (_sourceErrors.Length > 0)
        {
            _sourceFocusRequest++;
        }
    }

    private async Task ValidateSourceAsync()
    {
        if (_sourceFlowId is not { } id || HostId == 0 || _busy)
        {
            return;
        }

        var host = HostId;
        var generation = _sourceGeneration;
        var revision = _sourceRevision;
        var text = _sourceText;
        _busy = true;
        try
        {
            var outcome = await _flowsService.ValidateSourceAsync(
                new(host),
                id,
                text,
                CancellationToken.None
            );
            if (!SourceRequestCurrent(host, id, generation, revision, text))
            {
                return;
            }

            _sourceValidatedRevision = -1;
            var focusSource = outcome.Match(
                _ =>
                {
                    _sourceErrors = [];
                    _sourceValidatedRevision = revision;
                    _sourceStatus = "The candidate is valid. Save to replace the stored flow.";
                    return false;
                },
                invalid =>
                {
                    _sourceErrors = invalid
                        .Errors.Select(error => error.Path + ": " + error.Message)
                        .ToArray();
                    _sourceStatus = "Validation failed. The original is unchanged.";
                    return true;
                },
                _ =>
                {
                    _sourceErrors =
                    [
                        "This flow or channel is no longer available. Reopen it before saving.",
                    ];
                    return false;
                }
            );
            if (focusSource)
            {
                _sourceFocusRequest++;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private bool SourceRequestCurrent(
        int host,
        AutomationFlowId id,
        long generation,
        long revision,
        string text
    ) =>
        HostId == host
        && _sourceFlowId == id
        && _sourceGeneration == generation
        && _sourceRevision == revision
        && _sourceText == text;

    private async Task<bool> SaveSourceCoreAsync()
    {
        if (_sourceFlowId is not { } id || HostId == 0 || _busy || !_sourceValidated)
        {
            return false;
        }

        var host = HostId;
        var generation = _sourceGeneration;
        var revision = _sourceRevision;
        var text = _sourceText;
        _busy = true;
        try
        {
            var outcome = await _flowsService.SaveSourceAsync(
                new(host),
                id,
                text,
                CancellationToken.None
            );
            if (!SourceRequestCurrent(host, id, generation, revision, text))
            {
                return false;
            }

            if (outcome is AutomationFlowSaveOutcome.Saved saved)
            {
                _hasChanges = false;
                await LoadCoreAsync(saved.FlowId);
                _feedback = "Flow saved.";
                return true;
            }
            _sourceValidatedRevision = -1;
            _sourceErrors = outcome is AutomationFlowSaveOutcome.Invalid invalid
                ? invalid.Errors.Select(error => error.Message).ToArray()
                : ["This flow or channel is no longer available."];
            _sourceStatus = "Save failed. The candidate is retained and the original is unchanged.";
            _sourceFocusRequest++;
            return false;
        }
        finally
        {
            _busy = false;
        }
    }
}
