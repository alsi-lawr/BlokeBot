using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationCelTransformInput(
    AutomationPortId PortId,
    AutomationCelIdentifier Identifier,
    string DisplayName,
    AutomationConfigurationFieldId BindingFieldId,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    AutomationValue FixedValue
);

internal sealed record AutomationCelTransformOutput(
    AutomationPortId PortId,
    string DisplayName,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    string Source
);

internal sealed record AutomationCelTransformConfiguration(
    ImmutableArray<AutomationCelTransformInput> Inputs,
    ImmutableArray<AutomationCelTransformOutput> Outputs
) : AutomationConfiguration;

internal static partial class AutomationCelTransform
{
    internal const string FunctionName = "format_number";

    internal static IAutomationDefinition Definition(
        AutomationDefinitionId id,
        AutomationDisplayMetadata display
    ) =>
        new AutomationDefinition<AutomationCelTransformConfiguration>(
            new(
                id,
                AutomationNodeKind.Transform,
                AutomationDefinitionScope.Host,
                new(new(1), new(1)),
                display,
                [],
                [],
                [],
                AutomationActionCapabilities.None,
                AutomationActionRetrySafety.NotApplicable
            ),
            Parse,
            Validate,
            configuration => Descriptor(id, display, configuration),
            configurationShapeOwnedByParser: true
        );

    internal static AutomationPureHandlerContract HandlerContract(AutomationDefinitionId id) =>
        new(id, AutomationNodeKind.Transform, [], [], UsesEffectiveDescriptor: true);

    private static AutomationConfigurationParseResult Parse(JsonElement json)
    {
        if (
            !AutomationCelTransformDocumentSerializer.TryDeserialize<AutomationCelTransformDocument>(
                json,
                out var document
            )
        )
        {
            return Invalid("schema", "Declare the Transform inputs and outputs.");
        }

        var inputs = ImmutableArray.CreateBuilder<AutomationCelTransformInput>();
        foreach (var inputDocument in document.Inputs)
        {
            if (!TryParseInput(inputDocument, out var input))
            {
                return Invalid("schema", "Repair the persisted Transform input schema.");
            }

            inputs.Add(input);
        }

        var outputs = ImmutableArray.CreateBuilder<AutomationCelTransformOutput>();
        foreach (var outputDocument in document.Outputs)
        {
            if (!TryParseOutput(outputDocument, out var output))
            {
                return Invalid("schema", "Repair the persisted Transform output schema.");
            }

            outputs.Add(output);
        }

        return new AutomationConfigurationParseResult.Parsed(
            new AutomationCelTransformConfiguration(inputs.ToImmutable(), outputs.ToImmutable())
        );
    }

    private static AutomationValidationResult Validate(
        AutomationCelTransformConfiguration configuration
    )
    {
        if (configuration.Outputs.IsEmpty)
        {
            return InvalidResult("schema", "Declare at least one Transform output.");
        }

        if (
            HasDuplicates(configuration.Inputs.Select(static input => input.PortId.Value))
            || HasDuplicates(configuration.Inputs.Select(static input => input.Identifier.Value))
            || HasDuplicates(
                configuration.Inputs.Select(static input => input.BindingFieldId.Value)
            )
            || HasDuplicates(configuration.Outputs.Select(static output => output.PortId.Value))
            || configuration
                .Inputs.Select(static input => input.PortId)
                .Intersect(configuration.Outputs.Select(static output => output.PortId))
                .Any()
        )
        {
            return InvalidResult("schema", "Use unique, non-reused Transform identities.");
        }

        foreach (var input in configuration.Inputs)
        {
            if (
                string.IsNullOrWhiteSpace(input.DisplayName)
                || !AutomationCelSyntax.IsIdentifier(input.Identifier.Value)
                || AutomationCelSyntax.ReservedIdentifiers.Contains(input.Identifier.Value)
                || input.ValueType == AutomationPortValueType.Flow
                || !Enum.IsDefined(input.ValueType)
                || !Enum.IsDefined(input.Nullability)
                || !Matches(input.ValueType, input.Nullability, input.FixedValue)
            )
            {
                return InvalidResult(
                    input.BindingFieldId.Value,
                    "Repair this Transform input declaration."
                );
            }
        }

        var declaredInputs = configuration.Inputs.ToImmutableDictionary(
            static input => input.Identifier.Value,
            StringComparer.Ordinal
        );
        foreach (var output in configuration.Outputs)
        {
            if (
                string.IsNullOrWhiteSpace(output.DisplayName)
                || !Scalar(output.ValueType)
                || !Enum.IsDefined(output.Nullability)
                || string.IsNullOrWhiteSpace(output.Source)
                || !AutomationTransformCelService.ValidateOutput(output, declaredInputs)
            )
            {
                return AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Port(output.PortId),
                    "Repair this Transform output expression."
                );
            }
        }

        return AutomationValidationResult.Valid;
    }

    private static AutomationDefinitionDescriptor Descriptor(
        AutomationDefinitionId id,
        AutomationDisplayMetadata display,
        AutomationCelTransformConfiguration configuration
    ) =>
        new(
            id,
            AutomationNodeKind.Transform,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            display,
            [
                .. configuration.Inputs.Select(static input => new AutomationPortMetadata(
                    input.PortId,
                    input.DisplayName,
                    "Receives an exact typed Transform input.",
                    input.ValueType,
                    Nullability: input.Nullability,
                    BindingFieldId: input.BindingFieldId
                )),
            ],
            [
                .. configuration.Outputs.Select(static output => new AutomationPortMetadata(
                    output.PortId,
                    output.DisplayName,
                    "Supplies an exact typed Transform result.",
                    output.ValueType,
                    Nullability: output.Nullability
                )),
            ],
            [
                .. configuration.Inputs.Select(
                    static input => new AutomationConfigurationFieldMetadata(
                        input.BindingFieldId,
                        input.DisplayName,
                        "Retains the Transform input binding payload.",
                        new AutomationConfigurationFieldType.Data(input.ValueType),
                        input.Nullability == AutomationPortNullability.NonNullable
                    )
                ),
            ],
            AutomationActionCapabilities.None,
            AutomationActionRetrySafety.NotApplicable
        );
}
