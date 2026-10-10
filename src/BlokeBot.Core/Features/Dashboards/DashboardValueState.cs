using System.Diagnostics;
using BlokeBot.Core.Features.CustomCommands;

namespace BlokeBot.Core.Features.Dashboards;

internal enum DashboardValueUnavailableReason
{
    Unconfigured,
    Missing,
    FeatureDisabled,
    ViewerRequired,
    ViewerNotFound,
    ViewerUnavailable,
    StorageUnavailable,
    InvalidSelection,
}

internal abstract record DashboardValueState
{
    private DashboardValueState() { }

    internal TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<Unavailable, TResult> unavailable,
        Func<Rejected, TResult> rejected
    ) =>
        this switch
        {
            Available value => available(value),
            Unavailable value => unavailable(value),
            Rejected value => rejected(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Available(
        CustomValueDefinitionDraft Definition,
        CustomValueSnapshot Value,
        CustomCommandViewer? Viewer
    ) : DashboardValueState;

    internal sealed record Unavailable(DashboardValueUnavailableReason Reason)
        : DashboardValueState;

    internal sealed record Rejected(DashboardRejection Reason) : DashboardValueState;
}
