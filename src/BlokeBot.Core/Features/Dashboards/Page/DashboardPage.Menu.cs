using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

[JsonConverter(typeof(JsonStringEnumConverter<DashboardEditorMenuAction>))]
public enum DashboardEditorMenuAction
{
    Undo,
    Redo,
    Delete,
    Settings,
    Position,
    MoveLeft,
    MoveRight,
    Narrow,
    Widen,
}

public sealed record DashboardEditorMenuView(
    string Context,
    string Target,
    string History,
    bool Undo,
    bool Redo,
    bool Delete,
    bool Settings,
    bool Position,
    bool MoveLeft,
    bool MoveRight,
    bool Narrow,
    bool Widen,
    string Note
);

public partial class DashboardPage
{
    // Includes selection and transient ownership, not just the persisted document revision.
    private string _editorContext =>
        $"{HostId}|{Dashboard}|{_loadGeneration}|{_interactionGeneration}|{_gestureGeneration}|{_edit?.Generation}|{_arrangementKind}|{_selected}|{_editing}|{_loading}|{_busy}|{_confirming}|{_settingsGeneration}|{_settingsOpen}|{_disposed}";
    private bool _editorActionsAvailable =>
        _editing && _edit is not null && !_loading && !_disposed && !_busy && !_confirming;

    [JSInvokable]
    public async Task<DashboardEditorMenuView?> PrepareEditorMenuAsync(
        string context,
        bool hitGrid,
        Guid? widgetId
    )
    {
        DashboardEditorMenuView? result = null;
        await InvokeAsync(() =>
        {
            if (!_editorActionsAvailable || context != _editorContext)
            {
                return;
            }
            if (hitGrid)
            {
                if (widgetId is { } id && !_document.Widgets.Any(w => w.Id.Value == id))
                {
                    return;
                }
                var selection = widgetId is { } selected
                    ? new DashboardWidgetId(selected)
                    : (DashboardWidgetId?)null;
                if (_settingsOpen && _selected != selection)
                {
                    HideSettings();
                }
                _selected = selection;
                StateHasChanged();
            }
            var selectedWidget = _selectedWidget;
            var placement = selectedWidget is null ? null : Placement(selectedWidget.Id);
            result = new(
                _editorContext,
                _selectedWidget?.Label ?? "No selection",
                "Dashboard history",
                _edit!.CanUndo,
                _edit.CanRedo,
                selectedWidget is not null,
                selectedWidget is not null,
                selectedWidget is not null,
                placement is { Column: > 0 },
                placement is not null && placement.Column + placement.Width < _arrangement.Columns,
                placement is { Width: > 1 },
                placement is not null && placement.Column + placement.Width < _arrangement.Columns,
                "Delete removes the selected widget from both arrangements. Undo can restore it."
            );
        });
        return result;
    }

    [JSInvokable]
    public Task ApplyEditorMenuAsync(DashboardEditorMenuAction action, string context) =>
        InvokeAsync(async () =>
        {
            if (!_editorActionsAvailable || context != _editorContext)
            {
                return;
            }
            switch (action)
            {
                case DashboardEditorMenuAction.Undo when _edit!.CanUndo:
                    await UndoAsync();
                    break;
                case DashboardEditorMenuAction.Redo when _edit!.CanRedo:
                    await RedoAsync();
                    break;
                case DashboardEditorMenuAction.Delete when _selectedWidget is { } selected:
                    await RemoveAsync(selected.Id);
                    break;
                case DashboardEditorMenuAction.Settings when _selectedWidget is { } selected:
                    await SettingsAsync(selected.Id);
                    break;
                case DashboardEditorMenuAction.Position when _selectedWidget is { } selected:
                    await SettingsAsync(selected.Id, true);
                    break;
                case DashboardEditorMenuAction.MoveLeft
                or DashboardEditorMenuAction.MoveRight
                or DashboardEditorMenuAction.Narrow
                or DashboardEditorMenuAction.Widen when _selectedWidget is { } selected:
                    var placement = Placement(selected.Id);
                    var next = action switch
                    {
                        DashboardEditorMenuAction.MoveLeft when placement.Column > 0 =>
                            placement with
                            {
                                Column = placement.Column - 1,
                            },
                        DashboardEditorMenuAction.MoveRight
                            when placement.Column + placement.Width < _arrangement.Columns =>
                            placement with
                            {
                                Column = placement.Column + 1,
                            },
                        DashboardEditorMenuAction.Narrow when placement.Width > 1 => placement with
                        {
                            Width = placement.Width - 1,
                        },
                        DashboardEditorMenuAction.Widen
                            when placement.Column + placement.Width < _arrangement.Columns =>
                            placement with
                            {
                                Width = placement.Width + 1,
                            },
                        _ => placement,
                    };
                    if (next != placement)
                    {
                        _ = await ChangePlacementAsync(
                            selected,
                            next,
                            action
                                is DashboardEditorMenuAction.Narrow
                                    or DashboardEditorMenuAction.Widen
                        );
                    }
                    break;
            }
            StateHasChanged();
        });
}
