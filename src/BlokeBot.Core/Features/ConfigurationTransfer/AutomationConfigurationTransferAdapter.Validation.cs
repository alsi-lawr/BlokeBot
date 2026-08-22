namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private async Task<IReadOnlyList<AutomationTransferDiagnostic>> ValidateDraftsAsync(
        IEnumerable<MappedAutomationDraft> drafts,
        ICollection<ConfigurationValidationIssue>? previewIssues,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = new List<AutomationTransferDiagnostic>();
        foreach (var mapped in drafts)
        {
            diagnostics.AddRange(mapped.Diagnostics);
            var validation = await flows.ValidateConfigurationTransferAsync(
                mapped.Draft,
                cancellationToken
            );
            foreach (var error in validation.Errors)
            {
                var nodeId = error.NodeId is { } id
                    ? mapped.DiagnosticNodeLabels.GetValueOrDefault(
                        id,
                        AutomationTransferLabels.NoNode
                    )
                    : AutomationTransferLabels.NoNode;
                diagnostics.Add(Invalid(mapped.DiagnosticFlow, nodeId, error.Code));
                previewIssues?.Add(
                    new(
                        $"sections.automations.flows[{mapped.ImportedId}]",
                        error.Message,
                        BlocksApply: false
                    )
                );
            }
        }
        return diagnostics;
    }
}
