using Antlr4.Runtime.Tree;
using Cel.Internal;

namespace BlokeBot.Core.Features.Automations;

internal static partial class AutomationCelStaticTypes
{
    private static bool TryInfer(
        CelParser.ExprContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var branches = expression.conditionalOr();
        if (!TryInfer(branches[0], symbols, out var condition))
        {
            return false;
        }

        if (branches.Length == 1)
        {
            type = condition;
            return true;
        }

        return condition.ValueType == AutomationPortValueType.Boolean
            && !condition.CanBeNull
            && TryInfer(branches[1], symbols, out var whenTrue)
            && TryInfer(expression.expr(), symbols, out var whenFalse)
            && TryMerge(whenTrue, whenFalse, out type);
    }

    private static bool TryInfer(
        CelParser.ConditionalOrContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    ) => TryLogical(expression.conditionalAnd(), symbols, out type);

    private static bool TryInfer(
        CelParser.ConditionalAndContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    ) => TryLogical(expression.relation(), symbols, out type);

    private static bool TryLogical<T>(
        IReadOnlyList<T> operands,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
        where T : IParseTree
    {
        type = default;
        if (operands.Count == 1)
        {
            return TryInferNode(operands[0], symbols, out type);
        }

        foreach (var operand in operands)
        {
            if (
                !TryInferNode(operand, symbols, out var operandType)
                || operandType.ValueType != AutomationPortValueType.Boolean
                || operandType.CanBeNull
            )
            {
                return false;
            }
        }

        type = Boolean();
        return true;
    }

    private static bool TryInfer(
        CelParser.RelationContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var operands = expression.relation();
        if (operands.Length == 0)
        {
            return TryInfer(expression.calc(), symbols, out type);
        }

        if (
            operands.Length != 2
            || !TryInfer(operands[0], symbols, out var left)
            || !TryInfer(operands[1], symbols, out var right)
            || !Comparable(left, right, expression.op.Text)
        )
        {
            return false;
        }

        type = Boolean();
        return true;
    }

    private static bool TryInfer(
        CelParser.CalcContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var operands = expression.calc();
        if (operands.Length == 0)
        {
            return TryInfer(expression.unary(), symbols, out type);
        }

        if (
            operands.Length != 2
            || !TryInfer(operands[0], symbols, out var left)
            || !TryInfer(operands[1], symbols, out var right)
            || left.CanBeNull
            || right.CanBeNull
        )
        {
            return false;
        }

        if (
            expression.op.Text == "+"
            && left.ValueType == AutomationPortValueType.Text
            && right.ValueType == AutomationPortValueType.Text
        )
        {
            type = Text();
            return true;
        }

        if (
            left.ValueType != AutomationPortValueType.Number
            || right.ValueType != AutomationPortValueType.Number
        )
        {
            return false;
        }

        type = NumberResult(left, right);
        return type.NumberKind != AutomationCelNumberKind.None;
    }

    private static bool TryInfer(
        CelParser.UnaryContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        switch (expression)
        {
            case CelParser.MemberExprContext member:
                return TryInfer(member.member(), symbols, out type);
            case CelParser.LogicalNotContext logical:
                if (
                    !TryInfer(logical.member(), symbols, out var logicalType)
                    || logicalType.ValueType != AutomationPortValueType.Boolean
                    || logicalType.CanBeNull
                )
                {
                    return false;
                }

                type = Boolean();
                return true;
            case CelParser.NegateContext negate:
                if (
                    !TryInfer(negate.member(), symbols, out var number)
                    || number.ValueType != AutomationPortValueType.Number
                    || number.CanBeNull
                    || number.NumberKind == AutomationCelNumberKind.UnsignedInteger
                )
                {
                    return false;
                }

                type = number with
                {
                    IntegerConstant = number.IntegerConstant is { } constant ? -constant : null,
                };
                return true;
            default:
                return false;
        }
    }

    private static bool TryInfer(
        CelParser.MemberContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        switch (expression)
        {
            case CelParser.PrimaryExprContext primary:
                return TryInfer(primary.primary(), symbols, out type);
            case CelParser.SelectContext select:
                return AutomationCelSyntax.TryPath(select, out var path)
                    && symbols.TryGetValue(path, out type);
            case CelParser.IndexContext index:
                if (
                    !TryInfer(index.member(), symbols, out var indexed)
                    || indexed.ValueType != AutomationPortValueType.Arguments
                    || indexed.CanBeNull
                    || !TryInfer(index.expr(), symbols, out var key)
                    || key.NumberKind != AutomationCelNumberKind.Integer
                    || key.CanBeNull
                )
                {
                    return false;
                }

                type = Text();
                return true;
            case CelParser.MemberCallContext call:
                return TryMemberCall(call, symbols, out type);
            default:
                return false;
        }
    }

    private static bool TryInfer(
        CelParser.PrimaryContext expression,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        switch (expression)
        {
            case CelParser.IdentOrGlobalCallContext identifier when identifier.LPAREN() is null:
                return symbols.TryGetValue(identifier.id.Text, out type);
            case CelParser.IdentOrGlobalCallContext call:
                return TryGlobalCall(call, symbols, out type);
            case CelParser.ConstantLiteralContext constant:
                return TryLiteral(constant.literal(), out type);
            case CelParser.NestedContext nested:
                return TryInfer(nested.expr(), symbols, out type);
            default:
                return false;
        }
    }
}
