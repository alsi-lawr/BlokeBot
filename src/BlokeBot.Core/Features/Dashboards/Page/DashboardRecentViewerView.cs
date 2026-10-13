using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;

namespace BlokeBot.Core.Features.Dashboards.Page;

public sealed class DashboardRecentViewerView
{
    internal DashboardRecentViewerView(
        DashboardHostId host,
        DashboardResult<ImmutableArray<CustomCommandViewer>>? result
    ) => (Host, Result) = (host, result);

    internal DashboardHostId Host { get; }
    internal DashboardResult<ImmutableArray<CustomCommandViewer>>? Result { get; }
}
