using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationCelCompletion(string Name, AutomationCelStaticType Type)
{
    internal string TypeLabel =>
        $"{Type.ValueType} · {(Type.CanBeNull ? "can be null" : "required")}";
}

internal static class AutomationCelCompletions
{
    internal static ImmutableArray<string> OrdinaryFunctions { get; } =
    [
        "size(Text | Arguments) → Integer",
        "decimal(Text | Number) → Number (Decimal)",
        "int(Number) → Integer",
        "join(Arguments, Integer, Integer, Text) → Text",
    ];

    internal static ImmutableArray<string> TransformFunctions { get; } =
    [.. OrdinaryFunctions, "format_number(Decimal[, Integer 0..6]) → Text"];

    internal static ImmutableArray<AutomationCelCompletion> ForInput(AutomationEditorNode node) =>
        node.IsCelTransform
            ? SafeNames()
            :
            [
                .. SafeNames(),
                new("actor.login", new(AutomationPortValueType.Text, CanBeNull: true)),
                new("actor.display_name", new(AutomationPortValueType.Text, CanBeNull: true)),
                new("channel.login", new(AutomationPortValueType.Text, CanBeNull: false)),
                new("channel.display_name", new(AutomationPortValueType.Text, CanBeNull: false)),
            ];

    internal static ImmutableArray<AutomationCelCompletion> ForOutput(AutomationEditorNode node) =>
        [
            .. AutomationCelStaticTypes
                .ForInputs(node.TransformInputs)
                .Select(static symbol => new AutomationCelCompletion(symbol.Key, symbol.Value)),
        ];

    internal static ImmutableArray<string> InputExamples(
        AutomationEditorNode node,
        AutomationPortMetadata input
    ) =>
        [
            .. Examples(SafeNames(), input.ValueType, input.Nullability)
                .Select(example =>
                    AutomationDelayDurationBinding.IsInput(node.Definition, input)
                        ? $"{example} * 1000"
                        : example
                ),
        ];

    internal static ImmutableArray<string> OutputExamples(
        AutomationEditorNode node,
        AutomationCelTransformOutput output
    ) => [.. Examples(ForOutput(node), output.ValueType, output.Nullability)];

    private static ImmutableArray<AutomationCelCompletion> SafeNames() =>
        [
            .. AutomationCelStaticTypes
                .ForSafeView()
                .Select(static symbol => new AutomationCelCompletion(symbol.Key, symbol.Value)),
        ];

    private static IEnumerable<string> Examples(
        ImmutableArray<AutomationCelCompletion> names,
        AutomationPortValueType destination,
        AutomationPortNullability nullability
    )
    {
        foreach (var name in names)
        {
            if (name.Type.IsAssignableTo(destination, nullability))
            {
                yield return name.Name;
            }
            if (name.Type.CanBeNull)
            {
                continue;
            }
            if (name.Type.ValueType == AutomationPortValueType.Arguments)
            {
                if (destination == AutomationPortValueType.Boolean)
                {
                    yield return $"size({name.Name}) >= 2";
                }
                if (destination == AutomationPortValueType.Number)
                {
                    yield return $"decimal({name.Name}[0])";
                }
                if (destination == AutomationPortValueType.Text)
                {
                    yield return $"join({name.Name}, 1, size({name.Name}), \" \")";
                }
            }
            if (
                name.Type.ValueType == AutomationPortValueType.Text
                && destination == AutomationPortValueType.Number
            )
            {
                yield return $"decimal({name.Name})";
            }
        }
    }
}
