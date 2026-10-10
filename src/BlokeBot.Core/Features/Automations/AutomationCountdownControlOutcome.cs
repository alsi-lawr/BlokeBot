using System.Diagnostics;

namespace BlokeBot.Core.Features.Automations;

public enum AutomationCountdownState
{
    NotStarted,
    Running,
    Cancelled,
    Finished,
}

public sealed record AutomationCountdownSnapshot(
    string Name,
    AutomationCountdownState State,
    Guid? OccurrenceId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? DeadlineUtc
);

public abstract record AutomationCountdownControlOutcome
{
    private AutomationCountdownControlOutcome() { }

    public TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<Unavailable, TResult> unavailable
    ) =>
        this switch
        {
            Available value => available(value),
            Unavailable value => unavailable(value),
            _ => throw new UnreachableException(),
        };

    public sealed record Available(AutomationCountdownSnapshot Countdown)
        : AutomationCountdownControlOutcome;

    public sealed record Unavailable : AutomationCountdownControlOutcome;
}
