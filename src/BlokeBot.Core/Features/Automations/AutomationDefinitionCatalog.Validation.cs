namespace BlokeBot.Core.Features.Automations;

internal sealed partial class AutomationDefinitionCatalog
{
    internal static void Validate(
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

        ValidateFields(descriptor, moduleId);
        ValidatePorts(descriptor, moduleId, descriptor.Inputs, isInput: true);
        ValidatePorts(descriptor, moduleId, descriptor.Outputs, isInput: false);

        if (
            descriptor.Kind == AutomationNodeKind.Action
            && descriptor.Outputs.Any(static port => port.ValueType != AutomationPortValueType.Flow)
        )
        {
            throw Invalid(descriptor, moduleId, "Effectful actions cannot declare Data outputs.");
        }

        if (
            descriptor.Kind == AutomationNodeKind.Transform
            && descriptor.Inputs.Any(static port =>
                port.ValueType != AutomationPortValueType.Flow
                && port.Sensitivity != AutomationDataSensitivity.Safe
            )
        )
        {
            throw Invalid(descriptor, moduleId, "Transforms cannot accept Sensitive Data inputs.");
        }
    }
}
