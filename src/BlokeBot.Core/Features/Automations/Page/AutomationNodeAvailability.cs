namespace BlokeBot.Core.Features.Automations.Page;

internal readonly record struct AutomationNodeAvailability(bool IsAvailable, string Reason)
{
    internal static AutomationNodeAvailability Evaluate(
        AutomationDefinitionDescriptor definition,
        IReadOnlyList<AutomationEditorNode> nodes
    )
    {
        if (definition.TriggerContextRequirement is { } requirement)
        {
            var sources = nodes.Where(static node =>
                node.Definition.Kind == AutomationNodeKind.Source
            );
            return
                sources.Any()
                && sources.All(node => requirement.CompatibleSources.Contains(node.Definition.Id))
                ? new(true, AvailableReason(definition))
                : new(false, requirement.UnavailableReason);
        }

        return new(true, AvailableReason(definition));
    }

    private static string AvailableReason(AutomationDefinitionDescriptor definition) =>
        definition.Kind switch
        {
            AutomationNodeKind.Source => "Available for this channel.",
            AutomationNodeKind.Transform => "Available for declared node inputs.",
            AutomationNodeKind.Value => "Available in this flow.",
            AutomationNodeKind.Control or AutomationNodeKind.Action => "Available in this flow.",
        };
}
