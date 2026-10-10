using System.Diagnostics;

namespace BlokeBot.Core.Features.Dashboards;

internal abstract record DashboardGiveawayResult<T>
{
    private DashboardGiveawayResult() { }

    internal TResult Match<TResult>(
        Func<Ready, TResult> ready,
        Func<Unavailable, TResult> unavailable
    ) =>
        this switch
        {
            Ready value => ready(value),
            Unavailable value => unavailable(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Ready(T Value) : DashboardGiveawayResult<T>;

    internal sealed record Unavailable(DashboardBindingAvailability Reason)
        : DashboardGiveawayResult<T>;
}
