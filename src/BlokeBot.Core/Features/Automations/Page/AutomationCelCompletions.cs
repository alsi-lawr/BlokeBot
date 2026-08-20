using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationCelCompletion(string Name, AutomationPortValueType ValueType);

internal static class AutomationCelCompletions
{
    internal static ImmutableArray<AutomationCelCompletion> ForRestrictedInput(
        AutomationPortMetadata input
    ) =>
        input.ValueType == AutomationPortValueType.Arguments
            ? [new("arguments", AutomationPortValueType.Arguments)]
            : [];

    internal static ImmutableArray<AutomationCelCompletion> ForOutput(
        AutomationEditorNode transform
    ) =>
        transform
            .TransformInputs.SelectMany(static input => InputCompletions(input))
            .ToImmutableArray();

    private static IEnumerable<AutomationCelCompletion> InputCompletions(
        AutomationCelTransformInput input
    )
    {
        yield return new(input.Identifier.Value, input.ValueType);
        if (input.ValueType is AutomationPortValueType.Actor or AutomationPortValueType.Channel)
        {
            yield return new(
                $"{input.Identifier.Value}.display_name",
                AutomationPortValueType.Text
            );
            yield return new($"{input.Identifier.Value}.login", AutomationPortValueType.Text);
        }
        else if (input.ValueType == AutomationPortValueType.Stream)
        {
            yield return new($"{input.Identifier.Value}.title", AutomationPortValueType.Text);
            yield return new($"{input.Identifier.Value}.game_name", AutomationPortValueType.Text);
            yield return new(
                $"{input.Identifier.Value}.started_at",
                AutomationPortValueType.Timestamp
            );
        }
    }
}
