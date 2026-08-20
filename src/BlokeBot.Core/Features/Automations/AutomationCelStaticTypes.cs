using Cel;

namespace BlokeBot.Core.Features.Automations;

internal enum AutomationCelNumberKind
{
    None,
    Decimal,
    Integer,
    UnsignedInteger,
    Double,
}

internal readonly record struct AutomationCelStaticType(
    AutomationPortValueType? ValueType,
    bool CanBeNull,
    AutomationCelNumberKind NumberKind = AutomationCelNumberKind.None,
    long? IntegerConstant = null
)
{
    internal bool IsScalarOrNull =>
        ValueType
            is null
                or AutomationPortValueType.Text
                or AutomationPortValueType.Number
                or AutomationPortValueType.Boolean
                or AutomationPortValueType.Timestamp;

    internal bool IsAssignableTo(
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    ) =>
        (ValueType == valueType || (ValueType is null && CanBeNull))
        && (nullability == AutomationPortNullability.Nullable || !CanBeNull)
        && (
            valueType != AutomationPortValueType.Number
            || NumberKind != AutomationCelNumberKind.Double
        );
}

internal static partial class AutomationCelStaticTypes
{
    internal static IReadOnlyDictionary<string, AutomationCelStaticType> ForInputs(
        IEnumerable<AutomationCelTransformInput> inputs
    )
    {
        var symbols = new Dictionary<string, AutomationCelStaticType>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            AddSymbol(symbols, input.Identifier.Value, input.ValueType, input.Nullability);
        }

        return symbols;
    }

    internal static IReadOnlyDictionary<string, AutomationCelStaticType> ForSafeView()
    {
        var symbols = new Dictionary<string, AutomationCelStaticType>(StringComparer.Ordinal);
        foreach (var field in AutomationSafeTriggerView.Descriptor.Fields)
        {
            AddSymbol(symbols, field.Path, field.ValueType, field.Nullability);
        }

        return symbols;
    }

    internal static bool TryInfer(
        string source,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        try
        {
            return TryInfer(
                new CelEnvironment([], string.Empty).Parse(source).expr(),
                symbols,
                out type
            );
        }
        catch (CelException)
        {
            return false;
        }
    }

    private static void AddSymbol(
        IDictionary<string, AutomationCelStaticType> symbols,
        string name,
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    )
    {
        symbols.Add(name, From(valueType, nullability));
        var parentNullable = nullability == AutomationPortNullability.Nullable;
        switch (valueType)
        {
            case AutomationPortValueType.Actor:
            case AutomationPortValueType.Channel:
                symbols.Add($"{name}.login", Text(parentNullable));
                symbols.Add($"{name}.display_name", Text(parentNullable));
                break;
            case AutomationPortValueType.Stream:
                symbols.Add($"{name}.title", Text(canBeNull: true));
                symbols.Add($"{name}.game_name", Text(canBeNull: true));
                symbols.Add(
                    $"{name}.started_at",
                    new(AutomationPortValueType.Timestamp, CanBeNull: true)
                );
                break;
        }
    }

    private static AutomationCelStaticType From(
        AutomationPortValueType valueType,
        AutomationPortNullability nullability
    ) =>
        new(
            valueType,
            nullability == AutomationPortNullability.Nullable,
            valueType == AutomationPortValueType.Number
                ? AutomationCelNumberKind.Decimal
                : AutomationCelNumberKind.None
        );

    private static AutomationCelStaticType Text(bool canBeNull = false) =>
        new(AutomationPortValueType.Text, canBeNull);

    private static AutomationCelStaticType Boolean() =>
        new(AutomationPortValueType.Boolean, CanBeNull: false);
}
