using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public enum AutomationValueProvenance
{
    Generated,
    PublicDisplayName,
    PublicLogin,
    PublicChat,
}

public sealed record AutomationValueDiagnostic(
    AutomationPortId PortId,
    AutomationPortValueType ValueType,
    ImmutableArray<AutomationValueProvenance> Provenance,
    string DisplayValue
);

internal sealed record AutomationResolvedValue(
    AutomationValue Value,
    ImmutableArray<AutomationValueProvenance> Provenance
);

internal sealed record AutomationPurePortContract(
    AutomationPortId Id,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability
);

internal sealed record AutomationPureHandlerContract(
    AutomationDefinitionId DefinitionId,
    AutomationNodeKind Kind,
    ImmutableArray<AutomationPurePortContract> Inputs,
    ImmutableArray<AutomationPurePortContract> Outputs
);

internal sealed record AutomationPureNodeInput(
    AutomationConfiguration Configuration,
    ImmutableDictionary<AutomationPortId, AutomationResolvedValue> Inputs
);

internal abstract record AutomationPureNodeResult
{
    private AutomationPureNodeResult() { }

    internal sealed record Succeeded(
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> Outputs
    ) : AutomationPureNodeResult;

    internal sealed record Failed(string Code) : AutomationPureNodeResult;
}

internal interface IAutomationPureNodeHandler
{
    AutomationPureHandlerContract Contract { get; }

    ValueTask<AutomationPureNodeResult> ExecuteAsync(
        AutomationPureNodeInput input,
        CancellationToken cancellationToken
    );
}
