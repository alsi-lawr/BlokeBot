using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class AutomationDefinitionCatalog
{
    private static void ValidatePorts(
        AutomationDefinitionDescriptor descriptor,
        AutomationModuleId moduleId,
        ImmutableArray<AutomationPortMetadata> ports,
        bool isInput
    )
    {
        var direction = isInput ? "input" : "output";
        var ids = new HashSet<AutomationPortId>();
        foreach (var port in ports)
        {
            ValidateStableId(port.Id.Value, $"{direction} port");
            if (!ids.Add(port.Id))
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"The {direction} port identifier '{port.Id.Value}' is duplicated."
                );
            }

            if (
                string.IsNullOrWhiteSpace(port.Name)
                || string.IsNullOrWhiteSpace(port.Description)
                || !Enum.IsDefined(port.ValueType)
                || !Enum.IsDefined(port.Sensitivity)
                || !Enum.IsDefined(port.Nullability)
            )
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"The {direction} port '{port.Id.Value}' needs complete display metadata."
                );
            }

            if (
                port.ValueType == AutomationPortValueType.Flow
                && (
                    port.Sensitivity != AutomationDataSensitivity.Safe
                    || port.Nullability != AutomationPortNullability.NonNullable
                    || port.BindingFieldId is not null
                )
            )
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"The {direction} Flow port '{port.Id.Value}' cannot declare Data metadata."
                );
            }

            if (!isInput && port.BindingFieldId is not null)
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"The output port '{port.Id.Value}' cannot bind a configuration field."
                );
            }

            if (isInput && port.ValueType != AutomationPortValueType.Flow)
            {
                var field = descriptor.Configuration.SingleOrDefault(candidate =>
                    candidate.Id == port.BindingFieldId
                );
                if (
                    field is null
                    || FieldValueType(field.FieldType) != port.ValueType
                    || field.Sensitivity != port.Sensitivity
                    || (
                        field.Required
                            ? AutomationPortNullability.NonNullable
                            : AutomationPortNullability.Nullable
                    ) != port.Nullability
                )
                {
                    throw Invalid(
                        descriptor,
                        moduleId,
                        $"The Data input port '{port.Id.Value}' must bind a matching configuration field."
                    );
                }
            }
        }
    }

    private static AutomationPortValueType? FieldValueType(
        AutomationConfigurationFieldType fieldType
    ) =>
        fieldType switch
        {
            AutomationConfigurationFieldType.Text => AutomationPortValueType.Text,
            AutomationConfigurationFieldType.Choice => AutomationPortValueType.Text,
            AutomationConfigurationFieldType.Reference => AutomationPortValueType.Text,
            AutomationConfigurationFieldType.Number => AutomationPortValueType.Number,
            AutomationConfigurationFieldType.Duration => AutomationPortValueType.Number,
            AutomationConfigurationFieldType.Data data => data.ValueType,
            _ => null,
        };

    private static void ValidateFields(
        AutomationDefinitionDescriptor descriptor,
        AutomationModuleId moduleId
    )
    {
        var ids = new HashSet<AutomationConfigurationFieldId>();
        foreach (var field in descriptor.Configuration)
        {
            ValidateStableId(field.Id.Value, "configuration field");
            if (!ids.Add(field.Id))
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"Configuration field identifier '{field.Id.Value}' is duplicated."
                );
            }

            if (
                string.IsNullOrWhiteSpace(field.Name)
                || string.IsNullOrWhiteSpace(field.Description)
                || !Enum.IsDefined(field.Sensitivity)
            )
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"Configuration field '{field.Id.Value}' needs complete display metadata."
                );
            }

            var validType = field.FieldType switch
            {
                AutomationConfigurationFieldType.Text text => text.MaximumLength is null or > 0,
                AutomationConfigurationFieldType.Duration duration => duration.Minimum
                    > TimeSpan.Zero
                    && (duration.Maximum is null || duration.Maximum >= duration.Minimum),
                AutomationConfigurationFieldType.Number number => number.Maximum is null
                    || number.Maximum >= number.Minimum,
                AutomationConfigurationFieldType.Data data => data.ValueType
                    != AutomationPortValueType.Flow
                    && Enum.IsDefined(data.ValueType),
                AutomationConfigurationFieldType.Reference reference => Enum.IsDefined(
                    reference.ReferenceKind
                ),
                AutomationConfigurationFieldType.Choice choice => !choice.Values.IsEmpty
                    && choice.Values.All(static value => !string.IsNullOrWhiteSpace(value))
                    && choice.Values.Distinct(StringComparer.Ordinal).Count()
                        == choice.Values.Length,
                _ => false,
            };
            if (!validType)
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"Configuration field '{field.Id.Value}' has invalid type metadata."
                );
            }
        }
    }

    private static void ValidateStableId(string value, string subject)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > 96
            || value[0] is < 'a' or > 'z'
            || value.Any(static character =>
                character
                    is not (>= 'a' and <= 'z')
                        and not (>= '0' and <= '9')
                        and not '-'
                        and not '.'
            )
        )
        {
            throw new AutomationCatalogRegistrationException(
                $"Automation {subject} identifier '{value}' must use lowercase letters, numbers, dots, or hyphens and start with a letter."
            );
        }
    }

    private static AutomationCatalogRegistrationException Invalid(
        AutomationDefinitionDescriptor descriptor,
        AutomationModuleId moduleId,
        string reason
    ) =>
        new(
            $"Automation definition '{descriptor.Id.Value}' from module '{moduleId.Value}' is incompatible: {reason}"
        );
}
