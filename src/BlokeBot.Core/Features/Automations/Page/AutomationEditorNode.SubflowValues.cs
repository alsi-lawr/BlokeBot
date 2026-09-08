using System.Globalization;

namespace BlokeBot.Core.Features.Automations.Page;

public sealed partial class AutomationEditorNode
{
    internal AutomationValue? SubflowFixedValue(AutomationPortId port) =>
        Subflow?.FixedInputs.GetValueOrDefault(port)?.Value;

    internal bool TrySetSubflowFixedValue(AutomationPortId portId, string source)
    {
        var port = Definition.Inputs.FirstOrDefault(port => port.Id == portId);
        if (
            Subflow is null
            || port?.BindingFieldId is not { } field
            || !TryParseSubflowValue(source, port, out var value)
        )
        {
            return false;
        }
        Subflow = Subflow with
        {
            FixedInputs = Subflow.FixedInputs.SetItem(
                portId,
                new(value, [AutomationValueProvenance.Generated])
            ),
        };
        _values[field] = DisplayFixedValue(value);
        return true;
    }

    private static bool TryParseSubflowValue(
        string source,
        AutomationPortMetadata port,
        out AutomationValue value
    )
    {
        value = null!;
        if (
            port.Nullability == AutomationPortNullability.Nullable
            && string.IsNullOrWhiteSpace(source)
        )
        {
            value = new AutomationValue.Null(port.ValueType);
            return true;
        }
        switch (port.ValueType)
        {
            case AutomationPortValueType.Text:
                value = new AutomationValue.Text(source);
                return true;
            case AutomationPortValueType.Number
                when decimal.TryParse(
                    source,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var number
                ):
                value = new AutomationValue.Number(number);
                return true;
            case AutomationPortValueType.Boolean when bool.TryParse(source, out var boolean):
                value = new AutomationValue.Boolean(boolean);
                return true;
            case AutomationPortValueType.Timestamp
                when DateTimeOffset.TryParse(
                    source,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var timestamp
                ):
                value = new AutomationValue.Timestamp(timestamp);
                return true;
            default:
                return IsComplexFixedValue(port.ValueType)
                    && TryParseComplexFixedValue(
                        source,
                        port.ValueType,
                        port.Nullability,
                        out value
                    );
        }
    }
}
