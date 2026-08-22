using System.Text;

namespace BlokeBot.Plugins.Contracts;

public static class PluginValueValidator
{
    public static PluginValueValidationOutcome Validate(PluginValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var state = new ValidationState();
        Validate(value, "$", depth: 1, state);
        if (state.Bytes > PluginContractLimits.MaximumPluginValuePayloadBytes)
        {
            state.Errors.Add(new(PluginValueErrorCode.PayloadTooLarge, "$"));
        }

        return state.Errors.Count == 0
            ? new PluginValueValidationOutcome.Valid(state.Bytes)
            : new PluginValueValidationOutcome.Invalid(state.Errors.AsReadOnly());
    }

    private static void Validate(
        PluginValue value,
        string location,
        int depth,
        ValidationState state
    )
    {
        state.Nodes++;
        if (state.Nodes > PluginContractLimits.MaximumPluginValueNodes)
        {
            AddOnce(state, PluginValueErrorCode.NodeCountExceeded, location);
            return;
        }

        if (depth > PluginContractLimits.MaximumPluginValueDepth)
        {
            AddOnce(state, PluginValueErrorCode.DepthExceeded, location);
            return;
        }

        switch (value)
        {
            case PluginValue.Nil:
                state.Bytes++;
                break;
            case PluginValue.Boolean:
                state.Bytes++;
                break;
            case PluginValue.Number number:
                state.Bytes += sizeof(double);
                if (!double.IsFinite(number.Value))
                {
                    state.Errors.Add(new(PluginValueErrorCode.NonFiniteNumber, location));
                }
                break;
            case PluginValue.String text:
                AddString(text.Value, location, state);
                break;
            case PluginValue.Array array:
                for (var index = 0; index < array.Items.Length; index++)
                {
                    Validate(array.Items[index], $"{location}[{index}]", depth + 1, state);
                }
                break;
            case PluginValue.Map map:
                ValidateMap(map, location, depth, state);
                break;
        }
    }

    private static void ValidateMap(
        PluginValue.Map map,
        string location,
        int depth,
        ValidationState state
    )
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in map.Properties)
        {
            var propertyLocation = $"{location}.{property.Name}";
            if (
                string.IsNullOrWhiteSpace(property.Name)
                || property.Name.Length > PluginContractLimits.MaximumNameCharacters
                || property.Name.Any(char.IsControl)
            )
            {
                state.Errors.Add(new(PluginValueErrorCode.InvalidMapKey, propertyLocation));
            }
            else if (!keys.Add(property.Name))
            {
                state.Errors.Add(new(PluginValueErrorCode.DuplicateMapKey, propertyLocation));
            }

            AddString(property.Name, propertyLocation, state);
            Validate(property.Value, propertyLocation, depth + 1, state);
        }
    }

    private static void AddString(string value, string location, ValidationState state)
    {
        var bytes = Encoding.UTF8.GetByteCount(value);
        state.Bytes += bytes;
        if (bytes > PluginContractLimits.MaximumPluginValueStringBytes)
        {
            state.Errors.Add(new(PluginValueErrorCode.StringTooLarge, location));
        }
    }

    private static void AddOnce(ValidationState state, PluginValueErrorCode code, string location)
    {
        if (!state.Errors.Any(error => error.Code == code))
        {
            state.Errors.Add(new(code, location));
        }
    }

    private sealed class ValidationState
    {
        internal int Nodes { get; set; }

        internal long Bytes { get; set; }

        internal List<PluginValueError> Errors { get; } = [];
    }
}
