using System.Diagnostics;

namespace BlokeBot.Core.Features.Dashboards;

internal enum DashboardRejection
{
    Unauthorized,
    HostMismatch,
    Revoked,
    AuthorityUnavailable,
    Missing,
    Invalid,
    Conflict,
}

internal abstract record DashboardResult<T>
{
    private DashboardResult() { }

    internal TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    ) =>
        this switch
        {
            Succeeded value => succeeded(value),
            Rejected value => rejected(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Succeeded(T Value) : DashboardResult<T>;

    internal sealed record Rejected(DashboardRejection Reason) : DashboardResult<T>;
}

internal abstract record DashboardSaveOutcome
{
    private DashboardSaveOutcome() { }

    internal TResult Match<TResult>(
        Func<Saved, TResult> saved,
        Func<Conflict, TResult> conflict,
        Func<Rejected, TResult> rejected
    ) =>
        this switch
        {
            Saved value => saved(value),
            Conflict value => conflict(value),
            Rejected value => rejected(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Saved(DashboardSnapshot Dashboard) : DashboardSaveOutcome;

    internal sealed record Conflict(SaveDashboardCommand Attempted, DashboardSnapshot Current)
        : DashboardSaveOutcome;

    internal sealed record Rejected(SaveDashboardCommand Attempted, DashboardRejection Reason)
        : DashboardSaveOutcome;
}
