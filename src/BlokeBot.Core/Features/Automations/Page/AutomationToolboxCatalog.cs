using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal enum AutomationToolboxCategory
{
    Triggers,
    Values,
    Transforms,
    Control,
    Actions,
}

internal sealed record AutomationToolboxItem(
    AutomationDefinitionDescriptor Definition,
    AutomationToolboxCategory Category,
    bool IsAvailable,
    string Availability,
    int Relevance
);

internal static class AutomationToolboxCatalog
{
    internal static ImmutableArray<AutomationToolboxItem> Query(
        IEnumerable<AutomationDefinitionDescriptor> definitions,
        AutomationToolboxCategory activeCategory,
        string search,
        Func<AutomationDefinitionDescriptor, AutomationNodeAvailability> availability,
        IEnumerable<AutomationDefinitionDescriptor>? contextualDefinitions = null
    )
    {
        var query = search.Trim();
        var searching = query.Length > 0;
        var contexts = (contextualDefinitions ?? [])
            .GroupBy(static definition => definition.Id)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        return definitions
            .Select(definition =>
            {
                var state = availability(definition);
                return new AutomationToolboxItem(
                    definition,
                    Category(definition.Kind),
                    state.IsAvailable,
                    state.Reason,
                    Relevance(definition, contexts.GetValueOrDefault(definition.Id) ?? [], query)
                );
            })
            .Where(item =>
                searching ? item.Relevance < int.MaxValue : item.Category == activeCategory
            )
            .OrderBy(static item => item.Relevance)
            .ThenBy(static item => item.Category)
            .ThenBy(static item => item.Definition.Display.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Definition.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static string CategoryLabel(AutomationToolboxCategory category) =>
        category switch
        {
            AutomationToolboxCategory.Triggers => "Triggers",
            AutomationToolboxCategory.Values => "Values",
            AutomationToolboxCategory.Transforms => "Transforms",
            AutomationToolboxCategory.Control => "Control",
            AutomationToolboxCategory.Actions => "Actions",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    internal static AutomationToolboxCategory Category(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => AutomationToolboxCategory.Triggers,
            AutomationNodeKind.Value => AutomationToolboxCategory.Values,
            AutomationNodeKind.Transform => AutomationToolboxCategory.Transforms,
            AutomationNodeKind.Control => AutomationToolboxCategory.Control,
            AutomationNodeKind.Action => AutomationToolboxCategory.Actions,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static int Relevance(
        AutomationDefinitionDescriptor definition,
        IReadOnlyList<AutomationDefinitionDescriptor> contexts,
        string search
    )
    {
        var relevance = DefinitionRelevance(definition, search);
        return contexts.Aggregate(
            relevance,
            (current, context) => Math.Min(current, DefinitionRelevance(context, search))
        );
    }

    private static int DefinitionRelevance(AutomationDefinitionDescriptor definition, string search)
    {
        var name = definition.Display.Name;
        return search.Length == 0 ? 0
            : name.Equals(search, StringComparison.OrdinalIgnoreCase) ? 0
            : name.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains(search, StringComparison.OrdinalIgnoreCase) ? 2
            : MessagePurposeRelevance(definition.Id, search) is { } purposeRelevance
                ? purposeRelevance
            : definition.Display.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                ? 8
            : definition.Display.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ? 9
            : definition.Inputs.Any(port => PortMatches(port, search))
            || definition.Outputs.Any(port => PortMatches(port, search))
                ? 10
            : int.MaxValue;
    }

    private static int? MessagePurposeRelevance(AutomationDefinitionId id, string search) =>
        !search.Equals("message", StringComparison.OrdinalIgnoreCase) ? null
        : id == AutomationDefinitionIds.CelTransform ? 3
        : id == AutomationDefinitionIds.ChatNotificationSource ? 4
        : id == AutomationDefinitionIds.SendShoutoutAction ? 5
        : id == AutomationDefinitionIds.IncomingRaidSource ? 6
        : id == AutomationDefinitionIds.ConditionControl ? 7
        : null;

    private static bool PortMatches(AutomationPortMetadata port, string search) =>
        port.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || port.ValueType.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
}
