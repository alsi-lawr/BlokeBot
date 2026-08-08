using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationCatalogRegistrationException(string message) : Exception(message);

internal sealed class AutomationDefinitionCatalog
{
    internal const int SupportedSchemaVersion = 1;

    private readonly ImmutableDictionary<
        AutomationDefinitionId,
        IAutomationDefinition
    > _definitions;

    public AutomationDefinitionCatalog(IEnumerable<IAutomationCatalogModule> modules)
    {
        var definitions = ImmutableDictionary.CreateBuilder<
            AutomationDefinitionId,
            IAutomationDefinition
        >();
        var moduleIds = new HashSet<AutomationModuleId>();
        foreach (var module in modules)
        {
            ValidateStableId(module.Id.Value, "module");
            if (!moduleIds.Add(module.Id))
            {
                throw new AutomationCatalogRegistrationException(
                    $"Automation module identifier '{module.Id.Value}' is registered more than once."
                );
            }

            foreach (var definition in module.Definitions)
            {
                Validate(definition.Descriptor, module.Id);
                if (!definitions.TryAdd(definition.Descriptor.Id, definition))
                {
                    throw new AutomationCatalogRegistrationException(
                        $"Automation definition identifier '{definition.Descriptor.Id.Value}' is registered more than once."
                    );
                }
            }
        }

        _definitions = definitions.ToImmutable();
        Descriptors = _definitions
            .Values.Select(static definition => definition.Descriptor)
            .OrderBy(static definition => definition.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal ImmutableArray<AutomationDefinitionDescriptor> Descriptors { get; }

    internal bool TryResolve(AutomationDefinitionId id, out IAutomationDefinition definition) =>
        _definitions.TryGetValue(id, out definition!);

    private static void Validate(
        AutomationDefinitionDescriptor descriptor,
        AutomationModuleId moduleId
    )
    {
        ValidateStableId(descriptor.Id.Value, "definition");
        if (!Enum.IsDefined(descriptor.Kind))
        {
            throw Invalid(descriptor, moduleId, "The definition declares an unknown node kind.");
        }

        if (descriptor.Scope != AutomationDefinitionScope.Host)
        {
            throw Invalid(descriptor, moduleId, "Every automation definition must be host-scoped.");
        }

        if (
            descriptor.Schema.Current.Value is <= 0 or > SupportedSchemaVersion
            || descriptor.Schema.OldestReadable.Value <= 0
            || descriptor.Schema.OldestReadable.Value > descriptor.Schema.Current.Value
        )
        {
            throw Invalid(
                descriptor,
                moduleId,
                $"Schema versions must be within 1..{SupportedSchemaVersion} and the oldest readable version cannot exceed the current version."
            );
        }

        if (
            descriptor.Kind != AutomationNodeKind.Action
            && descriptor.Capabilities != AutomationActionCapabilities.None
        )
        {
            throw Invalid(
                descriptor,
                moduleId,
                "Only action definitions may declare action capabilities."
            );
        }

        if (
            !Enum.IsDefined(descriptor.RetrySafety)
            || (
                descriptor.Kind == AutomationNodeKind.Action
                    ? descriptor.RetrySafety == AutomationActionRetrySafety.NotApplicable
                    : descriptor.RetrySafety != AutomationActionRetrySafety.NotApplicable
            )
        )
        {
            throw Invalid(
                descriptor,
                moduleId,
                "Actions must declare retry safety and non-actions cannot declare it."
            );
        }

        var supportedCapabilities =
            AutomationActionCapabilities.SendsChat
            | AutomationActionCapabilities.PlaysOverlays
            | AutomationActionCapabilities.ChangesPoints
            | AutomationActionCapabilities.CallsTwitchApi
            | AutomationActionCapabilities.RunsScripts;
        if ((descriptor.Capabilities & ~supportedCapabilities) != 0)
        {
            throw Invalid(descriptor, moduleId, "The definition declares unknown capabilities.");
        }

        if (
            string.IsNullOrWhiteSpace(descriptor.Display.Name)
            || string.IsNullOrWhiteSpace(descriptor.Display.Description)
            || string.IsNullOrWhiteSpace(descriptor.Display.Category)
        )
        {
            throw Invalid(descriptor, moduleId, "Display metadata must be complete.");
        }

        ValidatePorts(descriptor, moduleId, descriptor.Inputs, "input");
        ValidatePorts(descriptor, moduleId, descriptor.Outputs, "output");
        ValidateFields(descriptor, moduleId);
    }

    private static void ValidatePorts(
        AutomationDefinitionDescriptor descriptor,
        AutomationModuleId moduleId,
        ImmutableArray<AutomationPortMetadata> ports,
        string direction
    )
    {
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
            )
            {
                throw Invalid(
                    descriptor,
                    moduleId,
                    $"The {direction} port '{port.Id.Value}' needs complete display metadata."
                );
            }
        }
    }

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
                AutomationConfigurationFieldType.Text text => text.MaximumLength > 0,
                AutomationConfigurationFieldType.Duration duration => duration.Minimum
                    > TimeSpan.Zero
                    && duration.Maximum >= duration.Minimum,
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
