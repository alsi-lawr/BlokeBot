namespace BlokeBot.Core.Features.Dashboards;

internal abstract record DashboardGuessingResult<T>
{
    private DashboardGuessingResult() { }

    internal abstract TResult Match<TResult>(
        Func<Ready, TResult> ready,
        Func<Unavailable, TResult> unavailable
    );

    internal sealed record Ready(T Value) : DashboardGuessingResult<T>
    {
        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<Unavailable, TResult> unavailable
        ) => ready(this);
    }

    internal sealed record Unavailable(DashboardBindingAvailability Reason)
        : DashboardGuessingResult<T>
    {
        internal override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}
