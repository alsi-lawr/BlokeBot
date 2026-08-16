using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public interface IAutomationDefinition
{
    AutomationDefinitionDescriptor Descriptor { get; }

    AutomationValidationResult Validate(AutomationConfiguration configuration);

    AutomationConfigurationParseResult Parse(JsonElement configuration);
}

internal interface IAutomationEffectiveDefinition
{
    bool UsesEffectiveDescriptor { get; }

    AutomationDefinitionDescriptor EffectiveDescriptor(AutomationConfiguration configuration);

    AutomationSafeTriggerSourceContract? SafeTriggerSource(AutomationConfiguration configuration);
}

public sealed class AutomationDefinition<TConfiguration>(
    AutomationDefinitionDescriptor descriptor,
    Func<JsonElement, AutomationConfigurationParseResult> parse,
    Func<TConfiguration, AutomationValidationResult> validate,
    Func<TConfiguration, AutomationDefinitionDescriptor>? effectiveDescriptor = null,
    Func<TConfiguration, AutomationSafeTriggerSourceContract?>? safeTriggerSource = null
) : IAutomationDefinition, IAutomationEffectiveDefinition
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

    AutomationDefinitionDescriptor IAutomationEffectiveDefinition.EffectiveDescriptor(
        AutomationConfiguration configuration
    ) =>
        configuration is TConfiguration typed && effectiveDescriptor is not null
            ? effectiveDescriptor(typed)
            : Descriptor;

    bool IAutomationEffectiveDefinition.UsesEffectiveDescriptor => effectiveDescriptor is not null;

    AutomationSafeTriggerSourceContract? IAutomationEffectiveDefinition.SafeTriggerSource(
        AutomationConfiguration configuration
    ) =>
        configuration is TConfiguration typed && safeTriggerSource is not null
            ? safeTriggerSource(typed)
            : null;
}

public interface IAutomationCatalogModule
{
    AutomationModuleId Id { get; }

    IEnumerable<IAutomationDefinition> Definitions { get; }
}
