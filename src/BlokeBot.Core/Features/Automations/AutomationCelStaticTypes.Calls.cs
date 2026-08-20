using Cel.Internal;

namespace BlokeBot.Core.Features.Automations;

internal static partial class AutomationCelStaticTypes
{
    private static bool TryGlobalCall(
        CelParser.IdentOrGlobalCallContext call,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        var arguments = call.exprList()?.expr() ?? [];
        var inferred = new AutomationCelStaticType[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!TryInfer(arguments[index], symbols, out inferred[index]))
            {
                return false;
            }
        }

        switch (call.id.Text)
        {
            case AutomationCelTransform.FunctionName
                when inferred.Length == 1
                    && inferred[0].NumberKind == AutomationCelNumberKind.Decimal
                    && !inferred[0].CanBeNull:
                type = Text();
                return true;
            case AutomationCelTransform.FunctionName
                when inferred.Length == 2
                    && inferred[0].NumberKind == AutomationCelNumberKind.Decimal
                    && !inferred[0].CanBeNull
                    && inferred[1].NumberKind == AutomationCelNumberKind.Integer
                    && (
                        inferred[1].IntegerConstant is null
                        || inferred[1].IntegerConstant is >= 0 and <= 6
                    ):
                type = Text();
                return true;
            case "timestamp" when OneNonNullable(inferred, AutomationPortValueType.Text):
                type = new(AutomationPortValueType.Timestamp, CanBeNull: false);
                return true;
            case "string" when inferred.Length == 1 && !inferred[0].CanBeNull:
                type = Text();
                return true;
            case "bool" when OneNonNullable(inferred, AutomationPortValueType.Boolean):
                type = Boolean();
                return true;
            case "decimal"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Decimal
                );
                return true;
            case "double"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Double
                );
                return true;
            case "int"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Integer
                );
                return true;
            case "uint"
                when inferred.Length == 1
                    && inferred[0].ValueType == AutomationPortValueType.Number
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.UnsignedInteger
                );
                return true;
            case "size"
                when inferred.Length == 1
                    && inferred[0].ValueType
                        is AutomationPortValueType.Text
                            or AutomationPortValueType.Arguments
                    && !inferred[0].CanBeNull:
                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Integer
                );
                return true;
            default:
                return false;
        }
    }

    private static bool TryMemberCall(
        CelParser.MemberCallContext call,
        IReadOnlyDictionary<string, AutomationCelStaticType> symbols,
        out AutomationCelStaticType type
    )
    {
        type = default;
        if (!TryInfer(call.member(), symbols, out var receiver) || receiver.CanBeNull)
        {
            return false;
        }

        var arguments = call.exprList()?.expr() ?? [];
        switch (call.id.Text)
        {
            case "size" when arguments.Length == 0:
                if (
                    receiver.ValueType
                    is not (AutomationPortValueType.Text or AutomationPortValueType.Arguments)
                )
                {
                    return false;
                }

                type = new(
                    AutomationPortValueType.Number,
                    CanBeNull: false,
                    AutomationCelNumberKind.Integer
                );
                return true;
            case "contains"
            or "endsWith"
            or "matches"
            or "startsWith"
                when receiver.ValueType == AutomationPortValueType.Text
                    && arguments.Length == 1
                    && TryInfer(arguments[0], symbols, out var argument)
                    && argument.ValueType == AutomationPortValueType.Text
                    && !argument.CanBeNull:
                type = Boolean();
                return true;
            default:
                return false;
        }
    }
}
