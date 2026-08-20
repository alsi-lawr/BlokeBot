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
    private ElementReference _searchInput;

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationDefinitionDescriptor> Definitions { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationEditorNode> Nodes { get; set; } = [];

    [Parameter]
    public EventCallback<AutomationDefinitionDescriptor> Add { get; set; }

    [Parameter]
    public EventCallback Close { get; set; }

    private ImmutableArray<AutomationToolboxItem> _items =>
        AutomationToolboxCatalog.Query(
            Definitions,
            _category,
            _search,
            Availability,
            Nodes.Select(static node => node.Definition)
        );

    private string _resultsTitle =>
        string.IsNullOrWhiteSpace(_search)
            ? AutomationToolboxCatalog.CategoryLabel(_category)
            : $"Results for “{_search.Trim()}”";

    private bool _searching => !string.IsNullOrWhiteSpace(_search);

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

    internal ValueTask FocusSearchAsync() => _searchInput.FocusAsync();

    private AutomationNodeAvailability Availability(AutomationDefinitionDescriptor definition) =>
        AutomationNodeAvailability.Evaluate(definition, Nodes);

    private static string AccessibleLabel(AutomationToolboxItem item) =>
        item.IsAvailable
            ? $"{item.Definition.Display.Name}. {item.Definition.Display.Description} Available."
            : $"{item.Definition.Display.Name}. {item.Definition.Display.Description} Unavailable. {item.Availability}";

    private static string ShortUnavailableReason(AutomationToolboxItem item) =>
        item.Definition.Id == AutomationDefinitionIds.SendShoutoutAction
            ? "Needs a known channel on each path."
            : "This node is not available in this flow.";

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
