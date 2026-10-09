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
}

internal interface IAutomationInputBindingDefinition
{
    AutomationConfigurationParseResult ParseForInputBindings(JsonElement configuration);

    AutomationValidationResult ValidateForInputBindings(
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding> bindings
    );
}

public sealed class AutomationDefinition<TConfiguration>
    : IAutomationDefinition,
        IAutomationEffectiveDefinition,
        IAutomationInputBindingDefinition
    where TConfiguration : AutomationConfiguration
{
    private readonly Func<JsonElement, AutomationConfigurationParseResult> _parse;
    private readonly Func<TConfiguration, AutomationValidationResult> _validate;
    private readonly Func<TConfiguration, AutomationDefinitionDescriptor>? _effectiveDescriptor;
    private readonly bool _configurationShapeOwnedByParser;
    private readonly IReadOnlySet<string> _configurationMembers;
    private readonly Func<JsonElement, AutomationConfigurationParseResult>? _parseForInputBindings;
    private readonly Func<
        TConfiguration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding>,
        AutomationValidationResult
    >? _validateForInputBindings;

    public AutomationDefinition(
        AutomationDefinitionDescriptor descriptor,
        Func<JsonElement, AutomationConfigurationParseResult> parse,
        Func<TConfiguration, AutomationValidationResult> validate
    )
        : this(descriptor, parse, validate, null, false) { }

    internal AutomationDefinition(
        AutomationDefinitionDescriptor descriptor,
        Func<JsonElement, AutomationConfigurationParseResult> parse,
        Func<TConfiguration, AutomationValidationResult> validate,
        Func<TConfiguration, AutomationDefinitionDescriptor>? effectiveDescriptor = null,
        bool configurationShapeOwnedByParser = false,
        Func<JsonElement, AutomationConfigurationParseResult>? parseForInputBindings = null,
        Func<
            TConfiguration,
            IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding>,
            AutomationValidationResult
        >? validateForInputBindings = null
    )
    {
        Descriptor = descriptor;
        _parse = parse;
        _validate = validate;
        _parseForInputBindings = parseForInputBindings;
        _validateForInputBindings = validateForInputBindings;
        _effectiveDescriptor = effectiveDescriptor;
        _configurationShapeOwnedByParser = configurationShapeOwnedByParser;
        _configurationMembers = descriptor
            .Configuration.Select(static field => field.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    public AutomationDefinitionDescriptor Descriptor { get; }

    public AutomationValidationResult Validate(AutomationConfiguration configuration) =>
        configuration is TConfiguration typed
            ? _validate(typed)
            : AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Definition(),
                $"Configuration does not match automation definition '{Descriptor.Id.Value}'."
            );

    public AutomationConfigurationParseResult Parse(JsonElement configuration) =>
        Parse(configuration, _parse);

    AutomationConfigurationParseResult IAutomationInputBindingDefinition.ParseForInputBindings(
        JsonElement configuration
    ) => Parse(configuration, _parseForInputBindings ?? _parse);

    AutomationValidationResult IAutomationInputBindingDefinition.ValidateForInputBindings(
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding> bindings
    ) =>
        configuration is TConfiguration typed && _validateForInputBindings is not null
            ? _validateForInputBindings(typed, bindings)
            : Validate(configuration);

    private AutomationConfigurationParseResult Parse(
        JsonElement configuration,
        Func<JsonElement, AutomationConfigurationParseResult> parse
    )
    {
        var shape = _configurationShapeOwnedByParser
            ? AutomationConfigurationShape.ValidateObject(Descriptor.Id, configuration)
            : AutomationConfigurationShape.ValidateObjectMembers(
                Descriptor.Id,
                configuration,
                _configurationMembers
            );
        return shape.IsValid
            ? parse(configuration)
            : new AutomationConfigurationParseResult.Invalid(shape.Errors);
    }

    AutomationDefinitionDescriptor IAutomationEffectiveDefinition.EffectiveDescriptor(
        AutomationConfiguration configuration
    ) =>
        configuration is TConfiguration typed && _effectiveDescriptor is not null
            ? _effectiveDescriptor(typed)
            : Descriptor;

    bool IAutomationEffectiveDefinition.UsesEffectiveDescriptor => _effectiveDescriptor is not null;
}

public interface IAutomationCatalogModule
{
    AutomationModuleId Id { get; }

    IEnumerable<IAutomationDefinition> Definitions { get; }
}
