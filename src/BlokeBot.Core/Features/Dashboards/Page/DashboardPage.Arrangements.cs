using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Dashboards.Page;

public partial class DashboardPage
{
    private DashboardArrangementKind _arrangementKind;
    private bool _arrangementChosen;
    private bool _showSlots = true;
    private DashboardArrangement _arrangement => _document.Arrangement(_arrangementKind);

    private DashboardPlacement Placement(DashboardWidgetId id) =>
        _arrangement.Placements.Single(p => p.WidgetId == id).Placement;

    private IEnumerable<DashboardWidget> _orderedWidgets =>
        _document
            .Widgets.OrderBy(w => Placement(w.Id).Row)
            .ThenBy(w => Placement(w.Id).Column)
            .ThenBy(w => Placement(w.Id).Order);
    private string _gridStyle =>
        $"--dashboard-columns: {_arrangement.Columns}; --dashboard-cell-height: {_arrangement.CellHeight}px; --dashboard-slot-rows: {(_arrangement.Placements.IsEmpty ? 4L : _arrangement.Placements.Max(p => (long)p.Placement.Row + p.Placement.Height) + 2)}";

    private async Task ArrangementChangedAsync(ChangeEventArgs args)
    {
        if (
            !Enum.TryParse<DashboardArrangementKind>(args.Value?.ToString(), out var kind)
            || !Enum.IsDefined(kind)
            || _busy
        )
        {
            return;
        }
        _arrangementChosen = true;
        await SwitchArrangementAsync(kind);
    }

    [JSInvokable]
    public async Task ViewportArrangementChanged(long generation, bool phone)
    {
        if (_disposed || generation != _loadGeneration || _arrangementChosen || _busy)
        {
            return;
        }
        await SwitchArrangementAsync(
            phone ? DashboardArrangementKind.Phone : DashboardArrangementKind.Desktop
        );
        if (!_disposed && generation == _loadGeneration)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task SwitchArrangementAsync(DashboardArrangementKind kind) =>
        kind == _arrangementKind
            ? Task.CompletedTask
            : CancelGestureAsync(() =>
            {
                _arrangementKind = kind;
                HideSettings();
            });

    private Task ColumnsChangedAsync(ChangeEventArgs args) => GridChangedAsync(args, true);

    private Task CellHeightChangedAsync(ChangeEventArgs args) => GridChangedAsync(args, false);

    private async Task GridChangedAsync(ChangeEventArgs args, bool columns)
    {
        if (!_editing || _edit is null || _busy)
        {
            return;
        }
        var parsed = int.TryParse(args.Value?.ToString(), out var value);
        var inRange = parsed && (columns ? value is >= 1 and <= 24 : value is >= 24 and <= 240);
        if (
            !inRange
            || !_edit.SetGrid(
                _arrangementKind,
                columns ? value : _arrangement.Columns,
                columns ? _arrangement.CellHeight : value
            )
        )
        {
            _feedback =
                "Grid unchanged. Every widget must fit; move or narrow widgets before reducing columns.";
            // A rejected native input must show the retained value, not the attempted value.
            if (_grid is not null)
            {
                await _grid.InvokeVoidAsync("restoreGridInputs");
            }
            return;
        }
        _feedback = null;
        await CancelGestureAsync(() => { });
    }
}
