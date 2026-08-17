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
    internal static ImmutableArray<AutomationToolboxCategory> Categories { get; } =
        Enum.GetValues<AutomationToolboxCategory>().ToImmutableArray();

    internal static ImmutableArray<AutomationToolboxItem> Query(
        IEnumerable<AutomationDefinitionDescriptor> definitions,
        AutomationToolboxCategory activeCategory,
        string search,
        Func<AutomationDefinitionDescriptor, (bool Available, string Reason)> availability
    )
    {
        var query = search.Trim();
        var searching = query.Length > 0;
        return definitions
            .Select(definition =>
            {
                var state = availability(definition);
                return new AutomationToolboxItem(
                    definition,
                    Category(definition.Kind),
                    state.Available,
                    state.Reason,
                    Relevance(definition, query)
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

    private static int Relevance(AutomationDefinitionDescriptor definition, string search)
    {
        var name = definition.Display.Name;
        return search.Length == 0 ? 0
            : name.Equals(search, StringComparison.OrdinalIgnoreCase) ? 0
            : name.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains(search, StringComparison.OrdinalIgnoreCase) ? 2
            : definition.Display.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                ? 3
            : definition.Display.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ? 4
            : definition.Inputs.Any(port => PortMatches(port, search))
            || definition.Outputs.Any(port => PortMatches(port, search))
                ? 5
            : int.MaxValue;
    }

    private static bool PortMatches(AutomationPortMetadata port, string search) =>
        port.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || port.ValueType.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
}

internal sealed record AutomationConnectionCompatibility(bool IsCompatible, string Reason)
{
    internal static AutomationConnectionCompatibility Compatible { get; } =
        new(true, "Correct type");
}

internal static class AutomationConnections
{
    internal static AutomationEdgeKind Kind(AutomationPortMetadata output) =>
        output.ValueType == AutomationPortValueType.Flow
            ? AutomationEdgeKind.Flow
            : AutomationEdgeKind.Data;

    internal static AutomationConnectionCompatibility Compatibility(
        AutomationNodeKind sourceKind,
        AutomationPortMetadata output,
        AutomationPortMetadata input
    ) =>
        (output.ValueType, input.ValueType, sourceKind) switch
        {
            (AutomationPortValueType.Flow, AutomationPortValueType.Flow, _) =>
                AutomationConnectionCompatibility.Compatible,
            (AutomationPortValueType.Flow, _, _) => new(
                false,
                "Flow outputs connect only to Flow inputs."
            ),
            (_, _, _)
                when input.ValueType == AutomationPortValueType.Flow
                    || output.ValueType != input.ValueType => new(
                false,
                $"Expected {TypeLabel(input)}. The selected source port supplies {TypeLabel(output)}."
            ),
            (_, _, AutomationNodeKind.Action) => new(
                false,
                "Use Data from a trigger, Value, Transform, or Control node."
            ),
            (_, _, _)
                when output.Nullability == AutomationPortNullability.Nullable
                    && input.Nullability == AutomationPortNullability.NonNullable => new(
                false,
                $"Expected {TypeLabel(input)}. The selected source can be null."
            ),
            (_, _, _)
                when output.Sensitivity == AutomationDataSensitivity.Sensitive
                    && input.Sensitivity == AutomationDataSensitivity.Safe => new(
                false,
                "This input cannot accept Sensitive Data."
            ),
            _ => AutomationConnectionCompatibility.Compatible,
        };

    internal static AutomationConnectionCompatibility Compatibility(
        AutomationEditorNode source,
        AutomationPortMetadata output,
        AutomationEditorNode target,
        AutomationPortMetadata input
    ) =>
        source.Id == target.Id
            ? new(false, "Choose a different node.")
            : Compatibility(source.Definition.Kind, output, input);

    internal static string? Issue(
        AutomationFlowDraftEdge edge,
        IReadOnlyList<AutomationEditorNode> nodes
    )
    {
        var source = nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var target = nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        if (source is null || target is null)
        {
            return "A saved node is not available.";
        }

        var output = source.Definition.Outputs.FirstOrDefault(port => port.Id == edge.SourcePortId);
        var input = target.Definition.Inputs.FirstOrDefault(port => port.Id == edge.TargetPortId);
        if (output is null || input is null)
        {
            return "A saved port is not available.";
        }
        if (Kind(output) != edge.Kind)
        {
            return edge.Kind == AutomationEdgeKind.Flow
                ? "The selected ports do not carry Flow."
                : "The selected ports do not carry Data.";
        }

        var compatibility = Compatibility(source, output, target, input);
        return compatibility.IsCompatible ? null : compatibility.Reason;
    }

    internal static string TypeLabel(AutomationPortMetadata port) =>
        $"{port.ValueType}{(port.Nullability == AutomationPortNullability.Nullable ? "?" : string.Empty)}";
}

internal sealed record AutomationCelCompletion(string Name, AutomationPortValueType ValueType);

internal static class AutomationCelCompletions
{
    internal static ImmutableArray<AutomationCelCompletion> ForRestrictedInput(
        AutomationPortMetadata input
    ) =>
        input.ValueType == AutomationPortValueType.Arguments
            ? [new("arguments", AutomationPortValueType.Arguments)]
            : [];

    internal static ImmutableArray<AutomationCelCompletion> ForOutput(
        AutomationEditorNode transform
    ) =>
        transform
            .TransformInputs.SelectMany(static input => InputCompletions(input))
            .ToImmutableArray();

    private static IEnumerable<AutomationCelCompletion> InputCompletions(
        AutomationCelTransformInput input
    )
    {
        yield return new(input.Identifier.Value, input.ValueType);
        if (input.ValueType is AutomationPortValueType.Actor or AutomationPortValueType.Channel)
        {
            yield return new(
                $"{input.Identifier.Value}.display_name",
                AutomationPortValueType.Text
            );
            yield return new($"{input.Identifier.Value}.login", AutomationPortValueType.Text);
        }
        else if (input.ValueType == AutomationPortValueType.Stream)
        {
            yield return new($"{input.Identifier.Value}.title", AutomationPortValueType.Text);
            yield return new($"{input.Identifier.Value}.game_name", AutomationPortValueType.Text);
            yield return new(
                $"{input.Identifier.Value}.started_at",
                AutomationPortValueType.Timestamp
            );
        }
    }
}
