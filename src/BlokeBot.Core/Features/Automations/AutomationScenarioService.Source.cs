using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    private static bool CanonicalSourceField(AutomationPortId fieldId) =>
        fieldId.Value is "actor" or "channel" or "arguments" or "stream" or "event-time";

    private static bool ReservedContextVariable(AutomationVariableName name) =>
        name.Value is "event" or "timestamps" || CanonicalSourceField(new(name.Value));

    private static AutomationVariable CanonicalSourceValue(
        AutomationPortId fieldId,
        AutomationContext context
    ) =>
        fieldId.Value switch
        {
            "actor" => new(
                context.Actor is { } actor
                    ? new AutomationValue.Actor(new(actor.Login, actor.DisplayName))
                    : new AutomationValue.Null(AutomationPortValueType.Actor),
                AutomationDataSensitivity.Safe
            ),
            "channel" => new(
                new AutomationValue.Channel(
                    new(context.Channel.Login, context.Channel.DisplayName)
                ),
                AutomationDataSensitivity.Safe
            ),
            "arguments" => new(
                context.Arguments.IsDefault
                    ? new AutomationValue.Null(AutomationPortValueType.Arguments)
                    : new AutomationValue.Arguments([
                        .. context.Arguments.Select(argument => new AutomationValueArgument(
                            argument.Position,
                            argument.Value,
                            [AutomationValueProvenance.PublicChat]
                        )),
                    ]),
                AutomationDataSensitivity.Safe
            ),
            "stream" => new(
                context.Stream is { } stream
                    ? new AutomationValue.Stream(
                        new(stream.Title, stream.GameName, stream.StartedAtUtc)
                    )
                    : new AutomationValue.Null(AutomationPortValueType.Stream),
                AutomationDataSensitivity.Sensitive
            ),
            "event-time" => new(
                new AutomationValue.Timestamp(context.Timestamps.OccurredAtUtc),
                AutomationDataSensitivity.Sensitive
            ),
            _ => throw new InvalidOperationException(
                "The source field is not part of the canonical context."
            ),
        };

    private static AutomationValue DefaultSourceValue(
        AutomationPortMetadata field,
        DateTimeOffset now
    ) =>
        field.Nullability == AutomationPortNullability.Nullable
            ? new AutomationValue.Null(field.ValueType)
            : field.ValueType switch
            {
                AutomationPortValueType.Text => new AutomationValue.Text("sample"),
                AutomationPortValueType.Number => new AutomationValue.Number(0),
                AutomationPortValueType.Boolean => new AutomationValue.Boolean(false),
                AutomationPortValueType.Timestamp => new AutomationValue.Timestamp(now),
                AutomationPortValueType.Actor => new AutomationValue.Actor(
                    new("sample_viewer", "Sample Viewer")
                ),
                AutomationPortValueType.Channel => new AutomationValue.Channel(
                    new("sample_channel", "Sample Channel")
                ),
                AutomationPortValueType.Stream => new AutomationValue.Stream(
                    new("Sample stream", "Just Chatting", now.AddHours(-1))
                ),
                AutomationPortValueType.Arguments => new AutomationValue.Arguments([]),
                AutomationPortValueType.Array => new AutomationValue.Array([]),
                AutomationPortValueType.Map => new AutomationValue.Map([]),
                AutomationPortValueType.Flow => throw new InvalidOperationException(
                    "Flow ports do not carry fixture values."
                ),
            };

    private void ValidateSourceContext(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var fields = SourceFields(draft, fixture.SourceNodeId);
        var variables = fixture.Context.Variables.ForExecution();
        foreach (var (name, _) in variables)
        {
            if (ReservedContextVariable(name) || !fields.Any(field => field.Id.Value == name.Value))
            {
                errors.Add(
                    new(
                        fixture.SourceNodeId,
                        "scenario-source-variable-invalid",
                        "Use a declared trigger field. Set canonical context values in their context fields, not Variables.",
                        PortId: new(name.Value)
                    )
                );
            }
        }
        foreach (var field in fields)
        {
            if (ReservedContextVariable(new(field.Id.Value)) && !CanonicalSourceField(field.Id))
            {
                errors.Add(
                    new(
                        fixture.SourceNodeId,
                        "scenario-source-variable-invalid",
                        "This trigger field conflicts with a canonical context identifier.",
                        PortId: field.Id
                    )
                );
                continue;
            }
            var variable = CanonicalSourceField(field.Id)
                ? CanonicalSourceValue(field.Id, fixture.Context)
                : variables.GetValueOrDefault(new(field.Id.Value));
            if (variable is null)
            {
                if (field.Nullability == AutomationPortNullability.NonNullable)
                {
                    errors.Add(
                        new(
                            fixture.SourceNodeId,
                            "scenario-source-value-required",
                            "Provide the value required by this trigger field.",
                            PortId: field.Id
                        )
                    );
                }
            }
            else if (
                !AutomationScenarioSerialization.ValidValue(
                    variable.Value,
                    field.Id.Value == "arguments" ? [AutomationValueProvenance.PublicChat] : default
                )
                || !AutomationDataResolver.Matches(field, variable.Value)
                || variable.Sensitivity != field.Sensitivity
            )
            {
                errors.Add(
                    new(
                        fixture.SourceNodeId,
                        "scenario-source-value-invalid",
                        "Use the trigger field's declared type, nullability and sensitivity.",
                        PortId: field.Id
                    )
                );
            }
        }
    }

    private AutomationContext SourceContext(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture
    ) =>
        fixture.Context with
        {
            Variables = new(
                fixture
                    .Context.Variables.ForExecution()
                    .Concat(
                        SourceFields(draft, fixture.SourceNodeId)
                            .Where(field =>
                                !ReservedContextVariable(new(field.Id.Value))
                                && field.Nullability == AutomationPortNullability.Nullable
                                && !fixture
                                    .Context.Variables.ForExecution()
                                    .ContainsKey(new(field.Id.Value))
                            )
                            .Select(field => new KeyValuePair<
                                AutomationVariableName,
                                AutomationVariable
                            >(
                                new(field.Id.Value),
                                new(new AutomationValue.Null(field.ValueType), field.Sensitivity)
                            ))
                    )
            ),
        };
}
