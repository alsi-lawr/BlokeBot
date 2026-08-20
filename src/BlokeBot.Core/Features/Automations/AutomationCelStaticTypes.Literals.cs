using System.Globalization;
using Antlr4.Runtime.Tree;
using Cel.Internal;

namespace BlokeBot.Core.Features.Automations;

internal static partial class AutomationCelStaticTypes
{
    private static bool TryLiteral(
        CelParser.LiteralContext literal,
        out AutomationCelStaticType type
    )
    {
        type = literal switch
        {
            CelParser.StringContext => Text(),
            CelParser.BoolTrueContext or CelParser.BoolFalseContext => Boolean(),
            CelParser.IntContext integer
                when long.TryParse(
                    integer.GetText(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value
                ) => new(
                AutomationPortValueType.Number,
                CanBeNull: false,
                AutomationCelNumberKind.Integer,
                value
            ),
            CelParser.UintContext => new(
                AutomationPortValueType.Number,
                CanBeNull: false,
                AutomationCelNumberKind.UnsignedInteger
            ),
            CelParser.DoubleContext => new(
                AutomationPortValueType.Number,
                CanBeNull: false,
                AutomationCelNumberKind.Double
            ),
            CelParser.NullContext => new(null, CanBeNull: true),
            _ => default,
        };
        return literal
            is CelParser.StringContext
                or CelParser.BoolTrueContext
                or CelParser.BoolFalseContext
                or CelParser.IntContext
                or CelParser.UintContext
                or CelParser.DoubleContext
                or CelParser.NullContext;
    }

    private static bool TryInferNode(
        IParseTree node,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    ) =>
        node switch
        {
            CelParser.RelationContext relation => TryInfer(relation, symbols, out type),
            CelParser.ConditionalAndContext conditionalAnd => TryInfer(
                conditionalAnd,
                symbols,
                out type
            ),
            _ => Fail(out type),
        };

    private static bool TryMerge(
        AutomationCelStaticType left,
        AutomationCelStaticType right,
        out AutomationCelStaticType type
    )
    {
        type = default;
        if (left.ValueType is null && left.CanBeNull)
        {
            type = right with { CanBeNull = true, IntegerConstant = null };
            return right.ValueType is not null;
        }

        if (right.ValueType is null && right.CanBeNull)
        {
            type = left with { CanBeNull = true, IntegerConstant = null };
            return left.ValueType is not null;
        }

        if (left.ValueType != right.ValueType)
        {
            return false;
        }

        var numberKind =
            left.NumberKind == right.NumberKind ? left.NumberKind
            : left.ValueType == AutomationPortValueType.Number ? AutomationCelNumberKind.None
            : left.NumberKind;
        if (
            left.ValueType == AutomationPortValueType.Number
            && numberKind == AutomationCelNumberKind.None
        )
        {
            return false;
        }

        type = left with
        {
            CanBeNull = left.CanBeNull || right.CanBeNull,
            NumberKind = numberKind,
            IntegerConstant =
                left.IntegerConstant == right.IntegerConstant ? left.IntegerConstant : null,
        };
        return true;
    }

    private static AutomationCelStaticType NumberResult(
        AutomationCelStaticType left,
        AutomationCelStaticType right
    )
    {
        var kind =
            left.NumberKind == AutomationCelNumberKind.Double
            || right.NumberKind == AutomationCelNumberKind.Double
                ? AutomationCelNumberKind.Double
            : left.NumberKind == AutomationCelNumberKind.Decimal
            || right.NumberKind == AutomationCelNumberKind.Decimal
                ? AutomationCelNumberKind.Decimal
            : left.NumberKind == right.NumberKind ? left.NumberKind
            : AutomationCelNumberKind.None;
        return new(AutomationPortValueType.Number, CanBeNull: false, kind);
    }

    private static bool Comparable(
        AutomationCelStaticType left,
        AutomationCelStaticType right,
        string operation
    ) =>
        operation is "==" or "!="
            ? left.ValueType == right.ValueType
                || (left.ValueType is null && left.CanBeNull)
                || (right.ValueType is null && right.CanBeNull)
            : !left.CanBeNull
                && !right.CanBeNull
                && left.ValueType == right.ValueType
                && left.ValueType
                    is AutomationPortValueType.Text
                        or AutomationPortValueType.Number
                        or AutomationPortValueType.Timestamp;

    private static bool OneNonNullable(
        IReadOnlyList<AutomationCelStaticType> arguments,
        AutomationPortValueType type
    ) => arguments.Count == 1 && arguments[0].ValueType == type && !arguments[0].CanBeNull;

    private static bool Fail(out AutomationCelStaticType type)
    {
        type = default;
        return false;
    }
}
