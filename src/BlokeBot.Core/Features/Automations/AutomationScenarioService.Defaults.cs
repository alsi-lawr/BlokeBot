using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    public AutomationScenarioFixture CreateDefaultFixture(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        ulong seed = 0
    )
    {
        var source = draft.Nodes.First(node => node.Id == sourceNodeId);
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        return new(
            sourceNodeId,
            new(source.Definition.TypeId),
            new(source.Definition.SchemaVersion),
            new(
                new(Guid.Empty, new(source.Definition.TypeId)),
                new("sample-viewer", "sample_viewer", "Sample Viewer"),
                new(draft.HostId, "sample-channel", "sample_channel", "Sample Channel"),
                new("sample-stream", "Sample stream", "Just Chatting", now.AddHours(-1)),
                new(now, now),
                [new(0, "sample")],
                new(
                    new Dictionary<AutomationVariableName, AutomationVariable>
                    {
                        [new("viewer_count")] = new(
                            new AutomationValue.Number(24),
                            AutomationDataSensitivity.Safe
                        ),
                        [new("bits")] = new(
                            new AutomationValue.Number(100),
                            AutomationDataSensitivity.Safe
                        ),
                        [new("category")] = new(
                            new AutomationValue.Text("Just Chatting"),
                            AutomationDataSensitivity.Safe
                        ),
                    }
                )
            ),
            now,
            seed,
            [],
            [],
            []
        );
    }

    public Task<AutomationScenarioRunOutcome> RunDefaultAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        CancellationToken cancellationToken
    ) => RunDefaultAsync(draft, sourceNodeId, 0, cancellationToken);

    internal Task<AutomationScenarioRunOutcome> RunDefaultAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        ulong seed,
        CancellationToken cancellationToken
    ) =>
        draft.Nodes.Any(node => node.Id == sourceNodeId)
            ? RunAsync(draft, CreateDefaultFixture(draft, sourceNodeId, seed), cancellationToken)
            : Task.FromResult<AutomationScenarioRunOutcome>(
                new AutomationScenarioRunOutcome.Invalid([
                    new(
                        sourceNodeId,
                        "scenario-source-invalid",
                        "Select a trigger node for the test."
                    ),
                ])
            );

    public ImmutableArray<AutomationPortMetadata> SourceFields(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId
    ) =>
        draft.Nodes.FirstOrDefault(node => node.Id == sourceNodeId) is { } source
        && catalog.ValidatePersistedDefinition(source.Definition)
            is AutomationConfigurationCheck.Valid
            {
                Definition.Kind: AutomationNodeKind.Source
            } valid
            ?
            [
                .. valid.Definition.Outputs.Where(port =>
                    port.ValueType != AutomationPortValueType.Flow
                ),
            ]
            : [];
}
