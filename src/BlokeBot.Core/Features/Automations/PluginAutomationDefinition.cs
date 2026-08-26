using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

internal sealed record PluginAutomationConfiguration(
    ImmutableDictionary<AutomationConfigurationFieldId, AutomationValue> Values
) : AutomationConfiguration;

internal interface IPluginAutomationDefinition : IAutomationDefinition
{
    PluginAutomationEndpoint Endpoint { get; }
}

internal sealed class PluginAutomationDefinition : IPluginAutomationDefinition
{
    private readonly ImmutableDictionary<
        AutomationConfigurationFieldId,
        PluginAutomationFieldDescriptor
    > _inputs;

    internal PluginAutomationDefinition(
        PluginAutomationEndpoint endpoint,
        AutomationDefinitionDescriptor descriptor
    )
    {
        Endpoint = endpoint;
        Descriptor = descriptor;
        _inputs = endpoint.Descriptor.Inputs.ToImmutableDictionary(
            input => new AutomationConfigurationFieldId(input.Id.Value)
        );
    }

    public PluginAutomationEndpoint Endpoint { get; }

    public AutomationDefinitionDescriptor Descriptor { get; }

    public AutomationValidationResult Validate(AutomationConfiguration configuration)
    {
        if (configuration is not PluginAutomationConfiguration plugin)
        {
            return AutomationValidationResult.Invalid(
                new AutomationValidationTarget.Definition(),
                $"Configuration does not match plugin automation definition '{Descriptor.Id.Value}'."
            );
        }

        foreach (var input in _inputs)
        {
            if (!plugin.Values.TryGetValue(input.Key, out var value))
            {
                continue;
            }

            if (!Matches(input.Value.ValueKind, value))
            {
                return Invalid(input.Key, "Enter a value with the declared plugin input type.");
            }
        }

        return AutomationValidationResult.Valid;
    }

    public AutomationConfigurationParseResult Parse(JsonElement configuration)
    {
        var shape = AutomationConfigurationShape.ValidateObjectMembers(
            Descriptor.Id,
            configuration,
            _inputs.Keys.Select(static id => id.Value).ToHashSet(StringComparer.Ordinal)
        );
        if (!shape.IsValid)
        {
            return new AutomationConfigurationParseResult.Invalid(shape.Errors);
        }

        var values = ImmutableDictionary.CreateBuilder<
            AutomationConfigurationFieldId,
            AutomationValue
        >();
        foreach (var input in _inputs)
        {
            if (!configuration.TryGetProperty(input.Key.Value, out var json))
            {
                continue;
            }

            if (json.ValueKind == JsonValueKind.Null && !input.Value.Required)
            {
                continue;
            }

            if (
                !AutomationStructuredValue.TryRead(json, out var value)
                || !Matches(input.Value.ValueKind, value)
            )
            {
                return InvalidParse(
                    input.Key,
                    "Enter a bounded JSON value with the declared plugin input type."
                );
            }
            values.Add(input.Key, value);
        }

        return new AutomationConfigurationParseResult.Parsed(
            new PluginAutomationConfiguration(values.ToImmutable())
        );
    }

    internal static AutomationPortValueType ValueType(PluginValueKind kind) =>
        kind switch
        {
            PluginValueKind.Boolean => AutomationPortValueType.Boolean,
            PluginValueKind.Number => AutomationPortValueType.Number,
            PluginValueKind.String => AutomationPortValueType.Text,
            PluginValueKind.Array => AutomationPortValueType.Array,
            PluginValueKind.Map => AutomationPortValueType.Map,
            PluginValueKind.Nil => throw new InvalidOperationException(
                "Nil is an absence sentinel, not an automation port type."
            ),
        };

    private static bool Matches(PluginValueKind expected, AutomationValue value) =>
        expected != PluginValueKind.Nil
        && AutomationPureHandlerRegistry.ValueType(value) == ValueType(expected);

    private static AutomationValidationResult Invalid(
        AutomationConfigurationFieldId fieldId,
        string message
    ) => AutomationValidationResult.Invalid(new AutomationValidationTarget.Field(fieldId), message);

    private static AutomationConfigurationParseResult InvalidParse(
        AutomationConfigurationFieldId fieldId,
        string message
    ) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(fieldId), message),
        ]);
}
