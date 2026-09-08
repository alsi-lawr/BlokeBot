using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationEditorSubflow(AutomationSubflowId Id, string Description);

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
        Subflow = Subflow is { } previous
            ? replacement.Subflow! with
            {
                FixedInputs = previous.FixedInputs,
            }
            : replacement.Subflow;
        foreach (var (key, value) in replacement._values)
        {
            _ = _values.TryAdd(key, value);
            _ = _bindings.TryAdd(key, new(AutomationInputBindingMode.Fixed, null));
        }
    }

    internal IEnumerable<AutomationConfigurationFieldId> UnmatchedSubflowInputs =>
        Subflow is null
            ? []
            : _bindings
                .Keys.Union(
                    Subflow.FixedInputs.Keys.Select(id => new AutomationConfigurationFieldId(
                        id.Value
                    ))
                )
                .Where(id => Definition.Configuration.All(item => item.Id != id));

    internal AutomationInputBinding? SubflowInputBinding(AutomationConfigurationFieldId field) =>
        _bindings.GetValueOrDefault(field);

    internal void RemoveUnmatchedSubflowInput(AutomationConfigurationFieldId field)
    {
        if (Subflow is null || Definition.Configuration.Any(value => value.Id == field))
        {
            return;
        }
        Subflow = Subflow with { FixedInputs = Subflow.FixedInputs.Remove(new(field.Value)) };
        _ = _values.Remove(field);
        _ = _bindings.Remove(field);
    }
}
