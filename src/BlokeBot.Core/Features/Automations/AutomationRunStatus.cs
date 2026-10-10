using System.Collections.Immutable;
using System.Diagnostics;

namespace BlokeBot.Core.Features.Automations;

public sealed record AutomationRunStatus(
    AutomationRunId Id,
    AutomationFlowId FlowId,
    AutomationFlowRunState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc
);

public abstract record AutomationRunStatusOutcome
{
    private AutomationRunStatusOutcome() { }

    public TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<HostNotFound, TResult> unavailable
    ) =>
        this switch
        {
            Available value => available(value),
            HostNotFound value => unavailable(value),
            _ => throw new UnreachableException(),
        };

    public sealed record Available(ImmutableArray<AutomationRunStatus> Runs)
        : AutomationRunStatusOutcome;

    public sealed record HostNotFound : AutomationRunStatusOutcome;
}
