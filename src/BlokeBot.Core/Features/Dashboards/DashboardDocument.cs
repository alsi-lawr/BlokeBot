using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Dashboards;

internal readonly record struct DashboardId(Guid Value);

internal readonly record struct DashboardWidgetId(Guid Value);

internal readonly record struct DashboardRevision(long Value);

internal readonly record struct DashboardHostId(int Value);

internal enum DashboardArrangementKind
{
    Desktop,
    Phone,
}

internal sealed record DashboardPlacement(int Column, int Row, int Width, int Height, int Order);

internal sealed record DashboardWidget(
    DashboardWidgetId Id,
    string Label,
    DashboardWidgetConfiguration Configuration
);

internal sealed record DashboardWidgetPlacement(
    DashboardWidgetId WidgetId,
    DashboardPlacement Placement
);

internal sealed record DashboardArrangement(
    int Columns,
    int CellHeight,
    ImmutableArray<DashboardWidgetPlacement> Placements
);

internal sealed record DashboardDocument(
    [property: JsonRequired] ImmutableArray<DashboardWidget> Widgets,
    [property: JsonRequired] DashboardArrangement Desktop,
    DashboardArrangement? Phone
)
{
    internal static DashboardDocument Empty { get; } = new([], new(12, 56, []), new(4, 56, []));

    internal DashboardArrangement Arrangement(DashboardArrangementKind kind) =>
        kind == DashboardArrangementKind.Desktop ? Desktop : Phone!;

    internal DashboardDocument WithArrangement(
        DashboardArrangementKind kind,
        DashboardArrangement arrangement
    ) =>
        kind == DashboardArrangementKind.Desktop
            ? this with
            {
                Desktop = arrangement,
            }
            : this with
            {
                Phone = arrangement,
            };

    internal DashboardDocument InitializePhone()
    {
        if (Phone is not null)
        {
            return this;
        }
        if (
            Widgets.IsDefault
            || Desktop is null
            || !DashboardArrangements.Valid(Desktop, Widgets.Select(w => w.Id).ToArray())
        )
        {
            return this;
        }
        var phone = new DashboardArrangement(4, Desktop.CellHeight, []);
        foreach (var widget in Widgets)
        {
            var original = Desktop.Placements.Single(p => p.WidgetId == widget.Id).Placement;
            var placement = DashboardArrangements.FirstFit(
                phone,
                Math.Min(original.Width, phone.Columns),
                original.Height,
                original.Order
            );
            if (placement is null)
            {
                return this;
            }
            phone = phone with { Placements = phone.Placements.Add(new(widget.Id, placement)) };
        }
        return this with { Phone = phone };
    }

    internal static DashboardDocument Create(
        int columns,
        params (DashboardWidget Widget, DashboardPlacement Placement)[] widgets
    ) =>
        new DashboardDocument(
            [.. widgets.Select(item => item.Widget)],
            new(
                columns,
                56,
                [
                    .. widgets.Select(item => new DashboardWidgetPlacement(
                        item.Widget.Id,
                        item.Placement
                    )),
                ]
            ),
            null
        ).InitializePhone();
}

internal sealed record DashboardSnapshot(
    DashboardId Id,
    string Name,
    DashboardRevision Revision,
    DashboardDocument Document
);

internal sealed record DashboardSummary(DashboardId Id, string Name, DashboardRevision Revision);

internal sealed record SaveDashboardCommand(
    DashboardHostId HostId,
    DashboardId Id,
    DashboardRevision ExpectedRevision,
    string Name,
    DashboardDocument Document
);
