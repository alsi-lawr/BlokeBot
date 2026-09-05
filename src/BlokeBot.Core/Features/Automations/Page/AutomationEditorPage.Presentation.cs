namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private AutomationEditorNode? _selectedNode =>
        _selectedNodeIds.Count == 1
            ? _editor?.Nodes.FirstOrDefault(node => node.Id == _selectedNodeIds.Single())
            : null;

    private AutomationValidationPresentation _validationPresentation =>
        _editor is null
            ? new([], _validationErrors.Length)
            : AutomationValidationPresentation.Create(
                _validationErrors,
                _editor.Nodes,
                _editor.Edges
            );

    private string _flowSubtitle =>
        _editor?.Id is null ? "Not saved"
        : _editor.IsEnabled ? "Enabled"
        : "Saved";

    private string _validationLabel =>
        _validated
            ? _validationErrors.IsEmpty
                ? "✓ Ready"
                : _validationPresentation.IssueCount == 1
                    ? "1 issue needs repair"
                    : $"{_validationPresentation.IssueCount} issues need repair"
            : "Draft";

    private string _validationClass =>
        _validated && !_validationErrors.IsEmpty
            ? "automation-validation automation-validation--invalid"
        : _validated ? "automation-validation automation-validation--ready"
        : "automation-validation";

    private string _impactWarning
    {
        get
        {
            var capabilities = CurrentCapabilities();
            var effects = new List<string>();
            if (capabilities.HasFlag(AutomationActionCapabilities.SendsChat))
            {
                effects.Add("send public chat messages");
            }
            if (capabilities.HasFlag(AutomationActionCapabilities.PlaysOverlays))
            {
                effects.Add("play overlay cues");
            }
            if (capabilities.HasFlag(AutomationActionCapabilities.ChangesPoints))
            {
                effects.Add("change channel points");
            }
            if (capabilities.HasFlag(AutomationActionCapabilities.CallsTwitchApi))
            {
                effects.Add("call Twitch");
            }
            return effects.Count == 0
                ? "It will respond to live events when enabled."
                : $"It can {string.Join(", ", effects)} when a live event reaches the connected action.";
        }
    }

    private string _runTitle =>
        _sampleOutcomes.IsEmpty
            ? _recentRuns.IsEmpty
                ? "No runs yet"
                : RecentRunTitle(_recentRuns[0])
            : _sampleOutcomes.Any(static outcome => outcome.State == AutomationNodeRunState.Failed)
                ? "Sample run failed"
                : "Last sample run completed";

    private string _runDescription =>
        _sampleOutcomes.IsEmpty
            ? _recentRuns.IsEmpty
                ? "Test this flow. The sample does not contact Twitch or run live actions."
                : RecentRunDescription(_recentRuns[0])
            : string.Join(
                " → ",
                _sampleOutcomes.Select(outcome =>
                    _editor?.Nodes.FirstOrDefault(node => node.Id == outcome.NodeId)?.EffectiveName
                    ?? "Unknown node"
                )
            ) + " · No live action was sent";

    private string _runIconClass =>
        _runTitle.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? "automation-run-icon automation-run-icon--failed"
            : "automation-run-icon";

    private string _runIcon =>
        _runTitle.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "!" : "✓";

    private string _editorClass
    {
        get
        {
            var classes = new List<string> { "automation-editor" };
            if (!_focusMode)
            {
                return classes[0];
            }

            classes.Add("automation-editor--focus");
            if (_editorToolsCollapsed)
            {
                classes.Add("automation-editor--tools-collapsed");
            }
            if (_runDrawerCollapsed)
            {
                classes.Add("automation-editor--runs-collapsed");
            }
            return string.Join(' ', classes);
        }
    }

    private string _editorBodyClass =>
        string.Join(
            ' ',
            new[]
            {
                "automation-editor-body",
                _focusMode && _flowRailCollapsed
                    ? "automation-editor-body--flows-collapsed"
                    : string.Empty,
                _nodeLibraryOpen ? "automation-editor-body--toolbox-open" : string.Empty,
            }.Where(static value => value.Length > 0)
        );
}
