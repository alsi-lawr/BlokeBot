using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Dashboards;

internal readonly record struct DashboardId(Guid Value);

internal readonly record struct DashboardWidgetId(Guid Value);

internal readonly record struct DashboardRevision(long Value);

internal readonly record struct DashboardHostId(int Value);

internal sealed record DashboardPlacement(int Column, int Row, int Width, int Height, int Order);

internal sealed record DashboardWidget(
    DashboardWidgetId Id,
    string Label,
    DashboardPlacement Placement,
    DashboardWidgetConfiguration Configuration
);

internal sealed record DashboardDocument(int Columns, ImmutableArray<DashboardWidget> Widgets)
{
    internal static DashboardDocument Empty { get; } = new(12, []);
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
