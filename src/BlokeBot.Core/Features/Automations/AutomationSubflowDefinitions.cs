using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationSubflowConfiguration(
    AutomationSubflowRevisionId? RevisionId,
    AutomationSubflowInterface Interface,
    ImmutableDictionary<AutomationPortId, AutomationResolvedValue> FixedInputs
) : AutomationConfiguration;

public static class AutomationSubflowDefinitions
{
    public const string Entry = "subflow-entry";
    public const string Exit = "subflow-exit";
    public const string Invoke = "subflow-invoke";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PersistedAutomationNodeDefinition Invocation(
        AutomationSubflowRevision revision,
        ImmutableDictionary<AutomationPortId, AutomationValue>? fixedInputs = null
    ) => Create(Invoke, revision.Interface, revision.Id, fixedInputs);

    public static PersistedAutomationNodeDefinition Boundary(
        string definitionId,
        AutomationSubflowInterface contract,
        ImmutableDictionary<AutomationPortId, AutomationValue>? fixedInputs = null
    ) => Create(definitionId, contract, null, fixedInputs);

    internal static PersistedAutomationNodeDefinition Create(
        string id,
        AutomationSubflowInterface contract,
        AutomationSubflowRevisionId? revision,
        ImmutableDictionary<AutomationPortId, AutomationValue>? fixedInputs
    ) =>
        new(
            id,
            1,
            JsonSerializer.SerializeToElement(
                new Document(
                    revision,
                    contract,
                    AutomationDataValueSerialization.SerializeOutputs(
                        (
                            fixedInputs
                            ?? ImmutableDictionary<AutomationPortId, AutomationValue>.Empty
                        ).ToImmutableDictionary(
                            pair => pair.Key,
                            pair => new AutomationResolvedValue(
                                pair.Value,
                                [AutomationValueProvenance.Generated]
                            )
                        )
                    )
                ),
                JsonOptions
            )
        );

    internal static IEnumerable<IAutomationDefinition> Definitions =>
        new[] { Entry, Exit, Invoke }.Select(
            id => new AutomationDefinition<AutomationSubflowConfiguration>(
                Descriptor(id, new([], [])),
                json => Parse(id, json),
                Validate,
                configuration => Descriptor(id, configuration.Interface),
                configurationShapeOwnedByParser: true
            )
        );

    internal static bool TryRead(
        PersistedAutomationNodeDefinition node,
        out AutomationSubflowConfiguration configuration
    )
    {
        if (
            node.TypeId is Entry or Exit or Invoke
            && Parse(node.TypeId, node.Configuration)
                is AutomationConfigurationParseResult.Parsed
                {
                    Configuration: AutomationSubflowConfiguration parsed
                }
        )
        {
            configuration = parsed;
            return Validate(parsed).IsValid;
        }
        configuration = null!;
        return false;
    }

    private static AutomationConfigurationParseResult Parse(string id, JsonElement json)
    {
        Document? document;
        try
        {
            document = json.Deserialize<Document>(JsonOptions);
        }
        catch (JsonException)
        {
            return Invalid();
        }
        return
            document?.Interface is null
            || document.FixedInputs is null
            || (
                id == Invoke
                    ? document.RevisionId is null || document.RevisionId.Value.Value == Guid.Empty
                    : document.RevisionId is not null
            )
            || AutomationDataValueSerialization.RestoreOutputs(document.FixedInputs)
                is not AutomationOutputRestoreOutcome.Available values
            ? Invalid()
            : new AutomationConfigurationParseResult.Parsed(
                new AutomationSubflowConfiguration(
                    document.RevisionId,
                    document.Interface,
                    values.Outputs
                )
            );
    }

    private static AutomationConfigurationParseResult Invalid() =>
        new AutomationConfigurationParseResult.Invalid([
            new(
                new AutomationValidationTarget.Definition(),
                "Select a valid immutable subflow interface."
            ),
        ]);

    internal static AutomationValidationResult Validate(
        AutomationSubflowConfiguration configuration
    ) =>
        ValidInterface(configuration.Interface)
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Definition(),
                "Declare unique bounded typed subflow ports."
            );

    internal static bool ValidInterface(AutomationSubflowInterface contract) =>
        !contract.Inputs.IsDefault
        && !contract.Outputs.IsDefault
        && contract.Inputs.Length <= 32
        && contract.Outputs.Length <= 32
        && ValidPorts(contract.Inputs)
        && ValidPorts(contract.Outputs);

    private static bool ValidPorts(ImmutableArray<AutomationPortMetadata> ports) =>
        ports.All(port => port is not null)
        && ports.Select(port => port.Id).Distinct().Count() == ports.Length
        && ports.All(port =>
            port is not null
            && StableId(port.Id.Value)
            && port.Id.Value is not ("flow" or "complete")
            && !string.IsNullOrWhiteSpace(port.Name)
            && port.Name.Length <= 200
            && !string.IsNullOrWhiteSpace(port.Description)
            && port.Description.Length <= 1000
            && port.ValueType != AutomationPortValueType.Flow
            && Enum.IsDefined(port.ValueType)
            && Enum.IsDefined(port.Sensitivity)
            && Enum.IsDefined(port.Nullability)
            && port.BindingFieldId is null
        );

    private static bool StableId(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 96
        && value[0] is >= 'a' and <= 'z'
        && value.All(character =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.'
        );

    internal static bool SameInterface(
        AutomationSubflowInterface left,
        AutomationSubflowInterface right
    ) => left.Inputs.SequenceEqual(right.Inputs) && left.Outputs.SequenceEqual(right.Outputs);

    internal static bool Compatible(
        AutomationSubflowInterface left,
        AutomationSubflowInterface right
    ) => SamePorts(left.Inputs, right.Inputs) && SamePorts(left.Outputs, right.Outputs);

    private static bool SamePorts(
        ImmutableArray<AutomationPortMetadata> left,
        ImmutableArray<AutomationPortMetadata> right
    ) =>
        left.Length == right.Length
        && left.All(port =>
            right.Any(candidate =>
                candidate.Id == port.Id
                && candidate.ValueType == port.ValueType
                && candidate.Sensitivity == port.Sensitivity
                && candidate.Nullability == port.Nullability
            )
        );

    private static AutomationDefinitionDescriptor Descriptor(
        string id,
        AutomationSubflowInterface contract
    )
    {
        var inputs =
            id == Entry ? ImmutableArray<AutomationPortMetadata>.Empty
            : id == Exit ? contract.Outputs
            : contract.Inputs;
        var outputs =
            id == Exit ? ImmutableArray<AutomationPortMetadata>.Empty
            : id == Entry ? contract.Inputs
            : contract.Outputs;
        return new(
            new(id),
            AutomationNodeKind.Control,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            new(
                id == Entry ? "Subflow entry"
                    : id == Exit ? "Subflow exit"
                    : "Call subflow",
                "Uses the selected typed subflow interface.",
                "Subflows"
            ),
            [
                .. id == Entry
                    ? []
                    : new[]
                    {
                        new AutomationPortMetadata(
                            new("flow"),
                            "Flow",
                            "Enters this node.",
                            AutomationPortValueType.Flow
                        ),
                    },
                .. inputs.Select(port => port with { BindingFieldId = new(port.Id.Value) }),
            ],
            [
                .. id == Exit
                    ? []
                    : new[]
                    {
                        new AutomationPortMetadata(
                            new("complete"),
                            "Complete",
                            "Continues this flow.",
                            AutomationPortValueType.Flow
                        ),
                    },
                .. outputs,
            ],
            [
                .. inputs.Select(port => new AutomationConfigurationFieldMetadata(
                    new(port.Id.Value),
                    port.Name,
                    port.Description,
                    new AutomationConfigurationFieldType.Data(port.ValueType),
                    port.Nullability == AutomationPortNullability.NonNullable,
                    port.Sensitivity
                )),
            ],
            AutomationActionCapabilities.None,
            AutomationActionRetrySafety.NotApplicable
        );
    }

    private sealed record Document(
        AutomationSubflowRevisionId? RevisionId,
        AutomationSubflowInterface Interface,
        string FixedInputs
    );
}
