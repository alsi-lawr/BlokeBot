using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationConfigurationShape
{
    internal static AutomationValidationResult ValidateObject(
        AutomationDefinitionId definitionId,
        JsonElement configuration
    ) =>
        configuration.ValueKind == JsonValueKind.Object
            ? AutomationValidationResult.Valid
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Definition(),
                $"Configuration for automation definition '{definitionId.Value}' must be a JSON object."
            );

    internal static AutomationValidationResult ValidateObjectMembers(
        AutomationDefinitionId definitionId,
        JsonElement configuration,
        IReadOnlySet<string> allowedMembers
    )
    {
        var objectShape = ValidateObject(definitionId, configuration);
        if (!objectShape.IsValid)
        {
            return objectShape;
        }

        foreach (var property in configuration.EnumerateObject())
        {
            if (!allowedMembers.Contains(property.Name))
            {
                return AutomationValidationResult.Invalid(
                    new AutomationValidationTarget.Field(new(property.Name)),
                    $"Configuration member '{property.Name}' is not supported by automation definition '{definitionId.Value}'."
                );
            }
        }

        return AutomationValidationResult.Valid;
    }
}
