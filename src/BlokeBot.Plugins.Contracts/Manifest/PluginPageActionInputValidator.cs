namespace BlokeBot.Plugins.Contracts;

public abstract record PluginPageActionInputValidationOutcome
{
    private PluginPageActionInputValidationOutcome() { }

    public sealed record Accepted(PluginValue.Map Input) : PluginPageActionInputValidationOutcome;

    public sealed record Rejected : PluginPageActionInputValidationOutcome;
}

public static class PluginPageActionInputValidator
{
    public static PluginPageActionInputValidationOutcome Validate(
        PluginActionDescriptor.Page action,
        PluginValue.Map input
    )
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(input);
        if (PluginValueValidator.Validate(input) is PluginValueValidationOutcome.Invalid)
        {
            return new PluginPageActionInputValidationOutcome.Rejected();
        }

        var fields = action.Inputs.ToDictionary(static field => field.Id.Value);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.Properties)
        {
            if (
                !present.Add(property.Name)
                || !fields.TryGetValue(property.Name, out var field)
                || Kind(property.Value) != field.ValueKind
            )
            {
                return new PluginPageActionInputValidationOutcome.Rejected();
            }
        }

        return action.Inputs.Any(field => field.Required && !present.Contains(field.Id.Value))
            ? new PluginPageActionInputValidationOutcome.Rejected()
            : new PluginPageActionInputValidationOutcome.Accepted(input);
    }

    private static PluginValueKind Kind(PluginValue value) =>
        value switch
        {
            PluginValue.Nil => PluginValueKind.Nil,
            PluginValue.Boolean => PluginValueKind.Boolean,
            PluginValue.Number => PluginValueKind.Number,
            PluginValue.String => PluginValueKind.String,
            PluginValue.Array => PluginValueKind.Array,
            PluginValue.Map => PluginValueKind.Map,
            _ => throw new InvalidOperationException("Unknown plugin value."),
        };
}
