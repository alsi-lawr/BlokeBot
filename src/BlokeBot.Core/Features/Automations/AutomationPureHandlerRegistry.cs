using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationPureHandlerRegistry
{
    private readonly ImmutableDictionary<
        AutomationDefinitionId,
        IAutomationPureNodeHandler
    > _handlers;

    internal AutomationPureHandlerRegistry(
        AutomationDefinitionCatalog catalog,
        IEnumerable<IAutomationPureNodeHandler> handlers
    )
    {
        var registered = ImmutableDictionary.CreateBuilder<
            AutomationDefinitionId,
            IAutomationPureNodeHandler
        >();
        foreach (var handler in handlers)
        {
            var contract = handler.Contract;
            if (!registered.TryAdd(contract.DefinitionId, handler))
            {
                throw new AutomationCatalogRegistrationException(
                    $"Automation handler identifier '{contract.DefinitionId.Value}' is registered more than once."
                );
            }

            if (
                !catalog.TryResolve(contract.DefinitionId, out var definition)
                || definition.Descriptor.Kind
                    is not (AutomationNodeKind.Value or AutomationNodeKind.Transform)
                || definition.Descriptor.Kind != contract.Kind
                || definition.Descriptor.Inputs.Any(static port =>
                    port.ValueType != AutomationPortValueType.Flow
                    && port.Sensitivity != AutomationDataSensitivity.Safe
                )
                || definition.Descriptor.Configuration.Any(static field =>
                    field.Sensitivity != AutomationDataSensitivity.Safe
                )
                || definition.Descriptor.Outputs.Any(static port =>
                    port.ValueType == AutomationPortValueType.Flow
                    || port.Sensitivity != AutomationDataSensitivity.Safe
                )
                || (
                    contract.UsesEffectiveDescriptor
                        ? definition is not IAutomationEffectiveDefinition
                            || !((IAutomationEffectiveDefinition)definition).UsesEffectiveDescriptor
                        : !Matches(definition.Descriptor.Inputs, contract.Inputs)
                            || !Matches(definition.Descriptor.Outputs, contract.Outputs)
                )
            )
            {
                throw new AutomationCatalogRegistrationException(
                    $"Automation handler '{contract.DefinitionId.Value}' does not match its pure definition descriptor."
                );
            }
        }

        _handlers = registered.ToImmutable();
    }

    internal bool TryResolve(
        AutomationDefinitionId definitionId,
        out IAutomationPureNodeHandler handler
    ) => _handlers.TryGetValue(definitionId, out handler!);

    internal static bool TryValidateResult(
        AutomationDefinitionDescriptor descriptor,
        AutomationPureNodeResult.Succeeded succeeded,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> inputs,
        out ImmutableDictionary<AutomationPortId, AutomationResolvedValue> outputs
    )
    {
        outputs = ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty;
        if (succeeded.Outputs.Count != descriptor.Outputs.Length)
        {
            return false;
        }

        ImmutableHashSet<AutomationValueProvenance> allowedProvenance =
            descriptor.Kind == AutomationNodeKind.Value
                ? [AutomationValueProvenance.Generated]
                : inputs
                    .Values.SelectMany(static value => value.Provenance)
                    .Append(AutomationValueProvenance.Generated)
                    .ToImmutableHashSet();
        var allowedSafeTriggerFields = inputs
            .Values.SelectMany(static value =>
                value.SafeTriggerFields.IsDefault ? [] : value.SafeTriggerFields
            )
            .ToImmutableHashSet();
        var validated = ImmutableDictionary.CreateBuilder<
            AutomationPortId,
            AutomationResolvedValue
        >();
        foreach (var port in descriptor.Outputs)
        {
            if (
                !succeeded.Outputs.TryGetValue(port.Id, out var output)
                || port.ValueType == AutomationPortValueType.Flow
                || port.Sensitivity != AutomationDataSensitivity.Safe
                || !Matches(port, output.Value)
                || !ValidProvenance(
                    output.Provenance,
                    allowedProvenance,
                    descriptor.Kind == AutomationNodeKind.Transform
                )
                || !ValidSafeTriggerFields(
                    output.SafeTriggerFields,
                    allowedSafeTriggerFields,
                    descriptor.Kind == AutomationNodeKind.Transform
                )
                || !ValidArguments(output, allowedProvenance)
            )
            {
                return false;
            }

            validated.Add(
                port.Id,
                output with
                {
                    Provenance = Normalize(output.Provenance),
                    SafeTriggerFields = Normalize(output.SafeTriggerFields),
                    ValueFreeDiagnostic =
                        descriptor.Kind == AutomationNodeKind.Transform
                        || output.ValueFreeDiagnostic,
                }
            );
        }

        outputs = validated.ToImmutable();
        return true;
    }

    internal static AutomationPortValueType ValueType(AutomationValue value) =>
        value switch
        {
            AutomationValue.Text => AutomationPortValueType.Text,
            AutomationValue.Number => AutomationPortValueType.Number,
            AutomationValue.Boolean => AutomationPortValueType.Boolean,
            AutomationValue.Timestamp => AutomationPortValueType.Timestamp,
            AutomationValue.Actor => AutomationPortValueType.Actor,
            AutomationValue.Channel => AutomationPortValueType.Channel,
            AutomationValue.Stream => AutomationPortValueType.Stream,
            AutomationValue.Arguments => AutomationPortValueType.Arguments,
            AutomationValue.Null nullValue => nullValue.ValueType,
            _ => throw new InvalidOperationException("Unknown automation value type."),
        };

    internal static bool ValidCheckpointShape(
        AutomationDefinitionDescriptor descriptor,
        IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue> outputs
    ) =>
        outputs.Count == descriptor.Outputs.Length
        && descriptor.Outputs.All(port =>
            port.ValueType != AutomationPortValueType.Flow
            && port.Sensitivity == AutomationDataSensitivity.Safe
            && outputs.TryGetValue(port.Id, out var output)
            && Matches(port, output.Value)
            && !output.Provenance.IsDefaultOrEmpty
            && output.Provenance.All(Enum.IsDefined)
            && (descriptor.Kind != AutomationNodeKind.Transform || output.ValueFreeDiagnostic)
            && (
                output.SafeTriggerFields.IsDefaultOrEmpty
                || output
                    .SafeTriggerFields.Select(static field => field.Value)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == output.SafeTriggerFields.Length
            )
        );

    private static bool Matches(
        ImmutableArray<AutomationPortMetadata> ports,
        ImmutableArray<AutomationPurePortContract> contracts
    ) =>
        ports.Length == contracts.Length
        && ports.All(port =>
            contracts.Any(contract =>
                contract.Id == port.Id
                && contract.ValueType == port.ValueType
                && contract.Nullability == port.Nullability
            )
        );

    private static bool Matches(AutomationPortMetadata port, AutomationValue value) =>
        value switch
        {
            AutomationValue.Null nullValue => port.Nullability == AutomationPortNullability.Nullable
                && nullValue.ValueType == port.ValueType
                && nullValue.ValueType != AutomationPortValueType.Flow,
            _ => ValueType(value) == port.ValueType,
        };

    private static bool ValidProvenance(
        ImmutableArray<AutomationValueProvenance> provenance,
        ImmutableHashSet<AutomationValueProvenance> allowed,
        bool exact
    ) =>
        !provenance.IsDefaultOrEmpty
        && provenance.All(Enum.IsDefined)
        && provenance.All(allowed.Contains)
        && (!exact || provenance.SequenceEqual(allowed.Order()));

    private static bool ValidArguments(
        AutomationResolvedValue output,
        ImmutableHashSet<AutomationValueProvenance> allowed
    )
    {
        if (output.Value is not AutomationValue.Arguments arguments)
        {
            return true;
        }

        if (arguments.Values.IsDefault)
        {
            return false;
        }

        if (arguments.Values.IsEmpty)
        {
            return true;
        }

        if (
            arguments.Values.Any(static argument => argument.Position < 0)
            || !arguments
                .Values.Select(static argument => argument.Position)
                .SequenceEqual(
                    arguments
                        .Values.Select(static argument => argument.Position)
                        .OrderBy(static position => position)
                        .Distinct()
                )
        )
        {
            return false;
        }

        var nested = arguments
            .Values.SelectMany(static argument => argument.Provenance)
            .ToImmutableHashSet();
        return arguments.Values.All(argument =>
                !argument.Provenance.IsDefaultOrEmpty
                && argument.Provenance.All(Enum.IsDefined)
                && argument.Provenance.All(allowed.Contains)
            ) && nested.SetEquals(output.Provenance);
    }

    private static ImmutableArray<AutomationValueProvenance> Normalize(
        ImmutableArray<AutomationValueProvenance> provenance
    ) => [.. provenance.Distinct().Order()];

    private static bool ValidSafeTriggerFields(
        ImmutableArray<AutomationSafeTriggerFieldId> fields,
        ImmutableHashSet<AutomationSafeTriggerFieldId> allowed,
        bool exact
    ) =>
        (fields.IsDefaultOrEmpty || fields.All(allowed.Contains))
        && (
            !exact
            || (
                fields.IsDefaultOrEmpty
                    ? allowed.IsEmpty
                    : fields.SequenceEqual(
                        allowed.OrderBy(static field => field.Value, StringComparer.Ordinal)
                    )
            )
        );

    private static ImmutableArray<AutomationSafeTriggerFieldId> Normalize(
        ImmutableArray<AutomationSafeTriggerFieldId> fields
    ) =>
        fields.IsDefaultOrEmpty
            ? []
            : [.. fields.Distinct().OrderBy(static field => field.Value, StringComparer.Ordinal)];
}
