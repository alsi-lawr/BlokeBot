namespace BlokeBot.Core.Features.Dashboards.Page;

public sealed class DashboardWidgetView
{
    internal DashboardWidgetView(DashboardWidget widget) => Widget = widget;

    internal DashboardWidget Widget { get; }
}
