using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationWorkspaceToolbar
{
    private ElementReference _toolboxButton;

    [Parameter]
    public bool ToolboxOpen { get; set; }

    [Parameter, EditorRequired]
    public AutomationFlowCanvasSettings Settings { get; set; }

    [Parameter]
    public AutomationEditorNode? SelectedNode { get; set; }

    [Parameter]
    public EventCallback ToggleToolbox { get; set; }

    [Parameter]
    public EventCallback FocusInspector { get; set; }

    [Parameter]
    public EventCallback<ChangeEventArgs> OrientationChanged { get; set; }

    [Parameter]
    public EventCallback<ChangeEventArgs> EdgeStyleChanged { get; set; }

    internal ValueTask FocusToolboxButtonAsync() => _toolboxButton.FocusAsync();
}
