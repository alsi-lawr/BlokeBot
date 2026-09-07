using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal abstract record AutomationSubflowConfiguration(
    AutomationSubflowInterface Interface,
    ImmutableDictionary<AutomationPortId, AutomationResolvedValue> FixedInputs
) : AutomationConfiguration;

internal sealed record AutomationSubflowInvocationConfiguration(
    AutomationSubflowId SubflowId,
    AutomationSubflowInterface Interface,
    ImmutableDictionary<AutomationPortId, AutomationResolvedValue> FixedInputs
) : AutomationSubflowConfiguration(Interface, FixedInputs);

internal sealed record AutomationSubflowBoundaryConfiguration(
    AutomationSubflowInterface Interface,
    ImmutableDictionary<AutomationPortId, AutomationResolvedValue> FixedInputs
) : AutomationSubflowConfiguration(Interface, FixedInputs);

internal sealed record AutomationFrozenSubflowConfiguration(
    AutomationSubflowRevisionId RevisionId,
    AutomationSubflowInterface Interface,
    ImmutableDictionary<AutomationPortId, AutomationResolvedValue> FixedInputs
) : AutomationSubflowConfiguration(Interface, FixedInputs);
