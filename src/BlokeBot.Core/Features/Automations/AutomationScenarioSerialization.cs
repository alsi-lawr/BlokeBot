using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationScenarioSerialization
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    internal static bool ValidValue(
        AutomationValue value,
        ImmutableArray<AutomationValueProvenance> provenance = default
    )
    {
        if (
            value is AutomationValue.Nil
            || (
                value is AutomationValue.Null nullValue
                && (
                    !Enum.IsDefined(nullValue.ValueType)
                    || nullValue.ValueType == AutomationPortValueType.Flow
                )
            )
        )
        {
            return false;
        }
        try
        {
            var json = AutomationDataValueSerialization.SerializeOutputs(
                new Dictionary<AutomationPortId, AutomationResolvedValue>
                {
                    [new("fixture")] = new(
                        value,
                        provenance.IsDefault ? [AutomationValueProvenance.Generated] : provenance
                    ),
                }
            );
            return AutomationDataValueSerialization.RestoreOutputs(json)
                is AutomationOutputRestoreOutcome.Available;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static string Serialize(AutomationScenarioFixture fixture) =>
        JsonSerializer.Serialize(
            new Document(
                fixture.SourceNodeId,
                fixture.SourceDefinitionId,
                fixture.SourceSchemaVersion,
                AutomationRuntimeSerialization.SerializeContext(fixture.Context),
                fixture.ClockUtc,
                fixture.Seed,
                [
                    .. fixture.ConnectedInputs.Select(input => new Input(
                        input.NodeId,
                        input.PortId,
                        input.Value.Sensitivity,
                        AutomationDataValueSerialization.SerializeOutputs(
                            new Dictionary<AutomationPortId, AutomationResolvedValue>
                            {
                                [input.PortId] = new(
                                    input.Value.Value,
                                    [AutomationValueProvenance.Generated]
                                ),
                            }
                        )
                    )),
                ],
                fixture.Configurations,
                fixture.Effects,
                fixture.SyntheticRecipe
            ),
            _options
        );

    internal static AutomationScenarioFixture Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, _options)!;
        var context = (AutomationContextRestoreOutcome.Available)
            AutomationRuntimeSerialization.RestoreContext(
                AutomationContextSchema.CurrentVersion,
                document.ContextJson
            );
        return new(
            document.SourceNodeId,
            document.SourceDefinitionId,
            document.SourceSchemaVersion,
            context.Context,
            document.ClockUtc,
            document.Seed,
            [
                .. document.ConnectedInputs.Select(input => new AutomationScenarioConnectedInput(
                    input.NodeId,
                    input.PortId,
                    new(
                        (
                            (AutomationOutputRestoreOutcome.Available)
                                AutomationDataValueSerialization.RestoreOutputs(input.ValueJson)
                        )
                            .Outputs[input.PortId]
                            .Value,
                        input.Sensitivity
                    )
                )),
            ],
            document.Configurations,
            document.Effects
        )
        {
            SyntheticRecipe = document.SyntheticRecipe,
        };
    }

    private sealed record Input(
        AutomationNodeId NodeId,
        AutomationPortId PortId,
        AutomationDataSensitivity Sensitivity,
        string ValueJson
    );

    private sealed record Document(
        AutomationNodeId SourceNodeId,
        AutomationDefinitionId SourceDefinitionId,
        AutomationSchemaVersion SourceSchemaVersion,
        string ContextJson,
        DateTimeOffset ClockUtc,
        ulong Seed,
        ImmutableArray<Input> ConnectedInputs,
        ImmutableArray<AutomationScenarioConfiguration> Configurations,
        ImmutableArray<AutomationScenarioEffect> Effects,
        AutomationScenarioSyntheticRecipe? SyntheticRecipe = null
    );
}
