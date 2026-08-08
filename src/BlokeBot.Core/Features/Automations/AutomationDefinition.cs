using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public interface IAutomationDefinition
{
    AutomationDefinitionDescriptor Descriptor { get; }

    AutomationValidationResult Validate(AutomationConfiguration configuration);

    AutomationConfigurationParseResult Parse(JsonElement configuration);
}

public sealed class AutomationDefinition<TConfiguration>(
    AutomationDefinitionDescriptor descriptor,
    Func<JsonElement, AutomationConfigurationParseResult> parse,
    Func<TConfiguration, AutomationValidationResult> validate
) : IAutomationDefinition
    where TConfiguration : AutomationConfiguration
{
    public AutomationDefinitionDescriptor Descriptor { get; } = descriptor;

    public AutomationValidationResult Validate(AutomationConfiguration configuration) =>
        configuration is TConfiguration typed
            ? validate(typed)
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Definition(),
                $"Configuration does not match automation definition '{Descriptor.Id.Value}'."
            );

    public AutomationConfigurationParseResult Parse(JsonElement configuration) =>
        parse(configuration);
}

public interface IAutomationCatalogModule
{
    AutomationModuleId Id { get; }

    IEnumerable<IAutomationDefinition> Definitions { get; }
}
