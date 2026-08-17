using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationToolbox
{
    private string _search = string.Empty;
    private AutomationToolboxCategory _category = AutomationToolboxCategory.Values;
    private ElementReference _triggerTab;
    private ElementReference _valueTab;
    private ElementReference _transformTab;
    private ElementReference _controlTab;
    private ElementReference _actionTab;

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationDefinitionDescriptor> Definitions { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationEditorNode> Nodes { get; set; } = [];

    [Parameter]
    public EventCallback<AutomationDefinitionDescriptor> Add { get; set; }

    [Parameter]
    public EventCallback Close { get; set; }

    private ImmutableArray<AutomationToolboxItem> _items =>
        AutomationToolboxCatalog.Query(Definitions, _category, _search, Availability);

    private string _resultsTitle =>
        string.IsNullOrWhiteSpace(_search)
            ? AutomationToolboxCatalog.CategoryLabel(_category)
            : $"Results for “{_search.Trim()}”";

    private string _activeTabId =>
        $"automation-toolbox-tab-{AutomationToolboxCatalog.CategoryLabel(_category).ToLowerInvariant()}";

    private void SearchChanged(ChangeEventArgs args) =>
        _search = args.Value?.ToString() ?? string.Empty;

    private void SelectCategory(AutomationToolboxCategory category) => _category = category;

    private async Task HandleTabKeyAsync(AutomationToolboxCategory category, KeyboardEventArgs args)
    {
        var index = Array.IndexOf(Enum.GetValues<AutomationToolboxCategory>(), category);
        var target = args.Key switch
        {
            "ArrowRight" => (index + 1) % 5,
            "ArrowLeft" => (index + 4) % 5,
            "Home" => 0,
            "End" => 4,
            _ => -1,
        };
        if (target < 0)
        {
            return;
        }

        _category = (AutomationToolboxCategory)target;
        await InvokeAsync(StateHasChanged);
        await TabReference(_category).FocusAsync();
    }

    private Task HandleKeyAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? Close.InvokeAsync() : Task.CompletedTask;

    private Task AddAsync(AutomationToolboxItem item) =>
        item.IsAvailable ? Add.InvokeAsync(item.Definition) : Task.CompletedTask;

    private (bool Available, string Reason) Availability(AutomationDefinitionDescriptor definition)
    {
        if (definition.TriggerContextRequirement is { } requirement)
        {
            var available = Nodes.Any(node =>
                requirement.CompatibleSources.Contains(node.Definition.Id)
            );
            return available
                ? (true, AvailableReason(definition))
                : (false, requirement.UnavailableReason);
        }

        return (true, AvailableReason(definition));
    }

    private static string AvailableReason(AutomationDefinitionDescriptor definition) =>
        definition.Kind switch
        {
            AutomationNodeKind.Source => "Available for this channel.",
            AutomationNodeKind.Transform => "Available for declared node inputs.",
            AutomationNodeKind.Value => "Available in this flow.",
            _ => "Available in this flow.",
        };

    private static string Description(AutomationDefinitionDescriptor definition) =>
        definition.Id == AutomationDefinitionIds.CelTransform
            ? "CEL is a small language that calculates a value from node inputs."
            : definition.Display.Description;

    private static string AvailabilityPrefix(AutomationToolboxItem item) =>
        item.IsAvailable ? "✓ " : "Unavailable — ";

    private static string KindAndTypes(AutomationDefinitionDescriptor definition)
    {
        var types = definition
            .Inputs.Concat(definition.Outputs)
            .Where(static port => port.ValueType != AutomationPortValueType.Flow)
            .Select(AutomationConnections.TypeLabel)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var kind = definition.Kind switch
        {
            AutomationNodeKind.Source => "Trigger",
            AutomationNodeKind.Value => "Value",
            AutomationNodeKind.Transform => "Transform",
            AutomationNodeKind.Control => "Control",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };
        return types.Length == 0 ? kind : $"{kind} · {string.Join(", ", types)}";
    }

    private static string PortSummary(AutomationDefinitionDescriptor definition)
    {
        var flowInputs = definition.Inputs.Count(static port =>
            port.ValueType == AutomationPortValueType.Flow
        );
        var flowOutputs = definition.Outputs.Count(static port =>
            port.ValueType == AutomationPortValueType.Flow
        );
        var dataInputs = definition.Inputs.Length - flowInputs;
        var dataOutputs = definition.Outputs.Length - flowOutputs;
        var parts = new List<string>();
        if (flowInputs + flowOutputs > 0)
        {
            parts.Add($"Flow {Direction(flowInputs, flowOutputs)}");
        }
        if (dataInputs > 0)
        {
            parts.Add($"{dataInputs} Data in");
        }
        if (dataOutputs > 0)
        {
            parts.Add($"{dataOutputs} Data out");
        }
        return parts.Count == 0 ? "No ports" : string.Join(" · ", parts);
    }

    private static string Direction(int inputs, int outputs) =>
        (inputs, outputs) switch
        {
            (> 0, > 0) => "in/out",
            (> 0, 0) => "in",
            _ => "out",
        };

    private string Selected(AutomationToolboxCategory category) =>
        _category == category ? "true" : "false";

    private int TabIndex(AutomationToolboxCategory category) => _category == category ? 0 : -1;

    private ElementReference TabReference(AutomationToolboxCategory category) =>
        category switch
        {
            AutomationToolboxCategory.Triggers => _triggerTab,
            AutomationToolboxCategory.Values => _valueTab,
            AutomationToolboxCategory.Transforms => _transformTab,
            AutomationToolboxCategory.Control => _controlTab,
            AutomationToolboxCategory.Actions => _actionTab,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
}
