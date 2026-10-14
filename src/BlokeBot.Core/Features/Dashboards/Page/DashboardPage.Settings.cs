using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardPage
{
    private bool _settingsOpen;
    private bool _positionSettings;
    private long _settingsGeneration;
    private string _settingsOwner =>
        $"{HostId}|{Dashboard}|{_loadGeneration}|{_interactionGeneration}|{_arrangementKind}|{_selected}|{_settingsGeneration}|{_settingsOpen}|{_disposed}";

    private void SelectWidget(DashboardWidgetId id)
    {
        if (_editorActionsAvailable && !_settingsOpen && _document.Widgets.Any(w => w.Id == id))
        {
            _selected = id;
        }
    }

    private void ShowSettings(DashboardWidgetId id, bool position = false)
    {
        _selected = id;
        _settingsOpen = true;
        _positionSettings = position;
        _focusSettings = true;
        ++_settingsGeneration;
    }

    private void HideSettings()
    {
        _settingsOpen = false;
        _focusSettings = false;
        ++_settingsGeneration;
    }

    private Task SettingsAsync(DashboardWidgetId id, bool position = false) =>
        _editing
        && _edit is not null
        && !_disposed
        && !_loading
        && !_busy
        && _document.Widgets.Any(w => w.Id == id)
            ? CancelGestureAsync(() => ShowSettings(id, position))
            : Task.CompletedTask;

    private Task ChangeSettingsPlacementAsync(
        DashboardWidget widget,
        DashboardPlacementTransport placement,
        string owner
    ) =>
        _settingsOpen && owner == _settingsOwner && _selected == widget.Id
            ? ChangePlacementAsync(widget, placement.ToPlacement(Placement(widget.Id).Order), true)
            : Task.CompletedTask;

    private Task CloseSettingsAsync(string owner)
    {
        if (!_disposed && !_loading && _settingsOpen && owner == _settingsOwner)
        {
            HideSettings();
        }
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task SettingsClosedAsync(string owner) =>
        InvokeAsync(() =>
        {
            if (!_disposed && !_loading && _settingsOpen && owner == _settingsOwner)
            {
                HideSettings();
                StateHasChanged();
            }
        });
}
