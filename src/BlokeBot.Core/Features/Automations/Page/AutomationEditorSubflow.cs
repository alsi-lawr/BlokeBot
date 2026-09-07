using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationEditorSubflow(
    AutomationSubflowId Id,
    string Description,
    AutomationSubflowRevisionId? BaseRevision
);

public sealed partial class AutomationEditorNode
{
    internal AutomationSubflowConfiguration? Subflow { get; private set; }

    internal static AutomationEditorNode FromDefinition(
        PersistedAutomationNodeDefinition persisted,
        AutomationCatalogService catalog,
        AutomationCanvasPosition position,
        string? name = null,
        AutomationNodeId? id = null
    )
    {
        var descriptor = (
            (AutomationConfigurationCheck.Valid)catalog.ValidatePersistedDefinition(persisted)
        ).Definition;
        return Restore(
            new(
                id ?? new(Guid.NewGuid()),
                persisted,
                AutomationExpressionLanguage.CurrentVersion,
                AutomationNodeFailurePolicy.Stop,
                ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty,
                position,
                name
            ),
            descriptor
        );
    }

    internal void ReplaceSubflowDefinition(
        PersistedAutomationNodeDefinition persisted,
        AutomationCatalogService catalog
    )
    {
        var replacement = FromDefinition(persisted, catalog, Position, DisplayAlias, Id);
        Definition = replacement.Definition;
        Subflow = replacement.Subflow;
        foreach (var key in _values.Keys.Except(replacement._values.Keys).ToArray())
        {
            _ = _values.Remove(key);
            _ = _bindings.Remove(key);
        }
        foreach (var (key, value) in replacement._values)
        {
            _ = _values.TryAdd(key, value);
            _ = _bindings.TryAdd(key, new(AutomationInputBindingMode.Fixed, null));
        }
    }
}
