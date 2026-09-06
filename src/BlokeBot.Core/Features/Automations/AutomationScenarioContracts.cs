using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public readonly record struct AutomationScenarioId(Guid Value);

public sealed record AutomationScenarioConnectedInput(
    AutomationNodeId NodeId,
    AutomationPortId PortId,
    AutomationVariable Value
);

public sealed record AutomationScenarioConfiguration(
    AutomationNodeId NodeId,
    PersistedAutomationNodeDefinition Definition
);

public enum AutomationScenarioEffectResult
{
    Succeeded,
    Failed,
}

public sealed record AutomationScenarioEffect(
    AutomationNodeId NodeId,
    AutomationScenarioEffectResult Result
);

public sealed record AutomationScenarioFixture(
    AutomationNodeId SourceNodeId,
    AutomationDefinitionId SourceDefinitionId,
    AutomationSchemaVersion SourceSchemaVersion,
    AutomationContext Context,
    DateTimeOffset ClockUtc,
    ulong Seed,
    ImmutableArray<AutomationScenarioConnectedInput> ConnectedInputs,
    ImmutableArray<AutomationScenarioConfiguration> Configurations,
    ImmutableArray<AutomationScenarioEffect> Effects
);

public sealed record AutomationScenarioSnapshot(
    AutomationScenarioId Id,
    AutomationFlowId FlowId,
    string Name,
    AutomationScenarioFixture Fixture
);

public enum AutomationScenarioAuthoringStatus
{
    Saved,
    Deleted,
    NotFound,
    LimitReached,
    Conflict,
    Invalid,
    FeatureDisabled,
    HostNotFound,
}

public sealed record AutomationScenarioAuthoringOutcome(
    AutomationScenarioAuthoringStatus Status,
    AutomationScenarioId? Id = null,
    ImmutableArray<AutomationGraphError> Errors = default
);
