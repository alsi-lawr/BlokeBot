using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed record AutomationScenarioGeneratedInput(
    AutomationNodeId NodeId,
    AutomationPortId PortId,
    AutomationPortValueType ValueType,
    AutomationPortNullability Nullability,
    AutomationDataSensitivity Sensitivity,
    AutomationValueProvenance Provenance
);

public sealed record AutomationScenarioSyntheticRecipe(
    ImmutableArray<AutomationScenarioGeneratedInput> Inputs,
    ImmutableArray<AutomationScenarioEffect> Effects
);

public sealed partial class AutomationScenarioService
{
    public AutomationScenarioFixture CreatePortableFixture(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        ulong seed = 0,
        DateTimeOffset? clockUtc = null,
        ImmutableArray<AutomationScenarioGeneratedInput> inputs = default,
        ImmutableArray<AutomationScenarioEffect> effects = default
    )
    {
        var fixture = CreateDefaultFixture(draft, sourceNodeId, seed);
        var clock = clockUtc ?? fixture.ClockUtc;
        var recipe = new AutomationScenarioSyntheticRecipe(
            inputs.IsDefault
                ? []
                :
                [
                    .. inputs
                        .OrderBy(input => input.NodeId.Value)
                        .ThenBy(input => input.PortId.Value, StringComparer.Ordinal),
                ],
            effects.IsDefault ? [] : [.. effects.OrderBy(effect => effect.NodeId.Value)]
        );
        return recipe.Inputs.Any(input =>
            input.Sensitivity != AutomationDataSensitivity.Safe
            || input.Provenance != AutomationValueProvenance.Generated
            || !Enum.IsDefined(input.ValueType)
            || input.ValueType == AutomationPortValueType.Flow
            || !Enum.IsDefined(input.Nullability)
        )
            ? throw new ArgumentException("Use generated safe typed input recipes.", nameof(inputs))
            : fixture with
            {
                ClockUtc = clock,
                Context = fixture.Context with
                {
                    Timestamps = new(clock, clock),
                    Stream = fixture.Context.Stream is { } stream
                        ? stream with
                        {
                            StartedAtUtc = clock.AddHours(-1),
                        }
                        : null,
                    Variables = new(
                        SourceFields(draft, sourceNodeId)
                            .Where(field => !ReservedContextVariable(new(field.Id.Value)))
                            .Select(field => new KeyValuePair<
                                AutomationVariableName,
                                AutomationVariable
                            >(
                                new(field.Id.Value),
                                new(DefaultSourceValue(field, clock), field.Sensitivity)
                            ))
                    ),
                },
                ConnectedInputs =
                [
                    .. recipe.Inputs.Select(input => new AutomationScenarioConnectedInput(
                        input.NodeId,
                        input.PortId,
                        new(GeneratedValue(input, clock), input.Sensitivity)
                    )),
                ],
                Effects = recipe.Effects,
                SyntheticRecipe = recipe,
            };
    }

    internal bool IsPortableFixture(AutomationFlowDraft draft, AutomationScenarioFixture fixture)
    {
        if (
            fixture.SyntheticRecipe is not { } recipe
            || recipe.Inputs.IsDefault
            || recipe.Effects.IsDefault
            || !fixture.Configurations.IsEmpty
        )
        {
            return false;
        }
        try
        {
            var generated = CreatePortableFixture(
                draft,
                fixture.SourceNodeId,
                fixture.Seed,
                fixture.ClockUtc,
                recipe.Inputs,
                recipe.Effects
            );
            return AutomationScenarioSerialization.Serialize(generated)
                == AutomationScenarioSerialization.Serialize(fixture);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static AutomationValue GeneratedValue(
        AutomationScenarioGeneratedInput input,
        DateTimeOffset clock
    ) =>
        input.Nullability == AutomationPortNullability.Nullable
            ? new AutomationValue.Null(input.ValueType)
            : input.ValueType switch
            {
                AutomationPortValueType.Text => new AutomationValue.Text("sample"),
                AutomationPortValueType.Number => new AutomationValue.Number(0),
                AutomationPortValueType.Boolean => new AutomationValue.Boolean(false),
                AutomationPortValueType.Timestamp => new AutomationValue.Timestamp(clock),
                AutomationPortValueType.Actor => new AutomationValue.Actor(
                    new("sample_viewer", "Sample Viewer")
                ),
                AutomationPortValueType.Channel => new AutomationValue.Channel(
                    new("sample_channel", "Sample Channel")
                ),
                AutomationPortValueType.Stream => new AutomationValue.Stream(
                    new("Sample stream", "Just Chatting", clock.AddHours(-1))
                ),
                AutomationPortValueType.Arguments => new AutomationValue.Arguments([]),
                AutomationPortValueType.Array => new AutomationValue.Array([]),
                AutomationPortValueType.Map => new AutomationValue.Map([]),
                AutomationPortValueType.Flow => throw new ArgumentException(
                    "Flow ports have no fixture value."
                ),
            };
}
