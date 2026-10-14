namespace BlokeBot.Core.Features.Dashboards;

internal static class DashboardArrangements
{
    internal static bool WithinBounds(DashboardPlacement p, int columns) =>
        p.Column >= 0
        && p.Row >= 0
        && p.Width > 0
        && p.Height > 0
        && p.Order >= 0
        && (long)p.Column + p.Width <= columns
        && (long)p.Row + p.Height <= int.MaxValue;

    internal static bool Overlaps(DashboardPlacement a, DashboardPlacement b) =>
        a.Column < (long)b.Column + b.Width
        && b.Column < (long)a.Column + a.Width
        && a.Row < (long)b.Row + b.Height
        && b.Row < (long)a.Row + a.Height;

    internal static bool Valid(
        DashboardArrangement arrangement,
        IReadOnlyCollection<DashboardWidgetId> ids
    ) =>
        arrangement.Columns is >= 1 and <= 24
        && arrangement.CellHeight is >= 24 and <= 240
        && !arrangement.Placements.IsDefault
        && arrangement.Placements.Length == ids.Count
        && arrangement.Placements.Select(p => p.WidgetId).Distinct().Count() == ids.Count
        && arrangement.Placements.All(p =>
            ids.Contains(p.WidgetId) && WithinBounds(p.Placement, arrangement.Columns)
        )
        && !arrangement
            .Placements.Where(
                (p, index) =>
                    arrangement
                        .Placements.Skip(index + 1)
                        .Any(other => Overlaps(p.Placement, other.Placement))
            )
            .Any();

    internal static DashboardPlacement? FirstFit(
        DashboardArrangement arrangement,
        int width,
        int height,
        int order
    )
    {
        // Only occupied row edges can change which columns fit. This avoids scanning a tall empty gap.
        var rows = arrangement
            .Placements.Select(p => p.Placement.Row + p.Placement.Height)
            .Prepend(0)
            .Distinct()
            .Order();
        foreach (var row in rows)
        {
            for (var column = 0; column <= arrangement.Columns - width; column++)
            {
                var candidate = new DashboardPlacement(column, row, width, height, order);
                if (
                    WithinBounds(candidate, arrangement.Columns)
                    && !arrangement.Placements.Any(p => Overlaps(p.Placement, candidate))
                )
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    internal static bool Equivalent(DashboardDocument a, DashboardDocument b) =>
        a.Widgets.SequenceEqual(b.Widgets)
        && Equivalent(a.Desktop, b.Desktop)
        && Equivalent(a.Phone!, b.Phone!);

    private static bool Equivalent(DashboardArrangement a, DashboardArrangement b) =>
        a.Columns == b.Columns
        && a.CellHeight == b.CellHeight
        && a.Placements.SequenceEqual(b.Placements);
}
