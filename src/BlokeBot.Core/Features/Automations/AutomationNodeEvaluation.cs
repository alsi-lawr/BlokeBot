namespace BlokeBot.Core.Features.Automations;

internal static class AutomationNodeEvaluation
{
    internal static AutomationNodeExecution End(DateTime now) =>
        new AutomationNodeExecution.Succeeded(
            "branch-ended",
            new AutomationOutputSelection.Selected([]),
            now
        );

    internal static AutomationNodeExecution Delay(
        DelayControlConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        DateTime now
    )
    {
        var duration = AutomationDelayDurationBinding
            .Resolve(configuration, inputs)
            .Match<TimeSpan?>(
                literal => literal.Value,
                milliseconds =>
                    milliseconds.Value > 0
                    && milliseconds.Value <= AutomationDelayDurationBinding.MaximumMilliseconds
                    && decimal.Truncate(milliseconds.Value) == milliseconds.Value
                        ? TimeSpan.FromTicks(
                            (long)milliseconds.Value * TimeSpan.TicksPerMillisecond
                        )
                        : null,
                invalid => null
            );
        return duration is not { } wait
                ? new AutomationNodeExecution.Failed("delay-invalid-duration")
            : wait <= DateTime.MaxValue - now
                ? new AutomationNodeExecution.Succeeded(
                    "delayed",
                    new AutomationOutputSelection.All(),
                    now + wait
                )
            : new AutomationNodeExecution.Failed("delay-unrepresentable");
    }

    internal static AutomationNodeExecution Condition(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        DateTime now
    ) =>
        inputs.GetValueOrDefault(new("predicate"))?.Value switch
        {
            AutomationValue.Boolean { Value: var result } => new AutomationNodeExecution.Succeeded(
                result ? "condition-true" : "condition-false",
                new AutomationOutputSelection.Selected([new(result ? "yes" : "no")]),
                now
            ),
            _ => new AutomationNodeExecution.Failed("condition-invalid"),
        };

    internal static AutomationNodeExecution Action(
        AutomationActionOutcome outcome,
        DateTime now,
        string successCode
    ) =>
        outcome.Match<AutomationNodeExecution>(
            _ => new AutomationNodeExecution.Succeeded(
                successCode,
                new AutomationOutputSelection.Selected([new("complete")]),
                now
            ),
            _ => new AutomationNodeExecution.Succeeded(
                "countdown-started",
                new AutomationOutputSelection.Selected([new("complete"), new("started")]),
                now
            ),
            _ => new AutomationNodeExecution.Succeeded(
                "countdown-already-running",
                new AutomationOutputSelection.Selected([new("complete"), new("already-running")]),
                now
            ),
            static failed => new AutomationNodeExecution.Failed(failed.Code)
        );
}

internal abstract record AutomationNodeExecution
{
    private AutomationNodeExecution() { }

    internal abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Failed, TResult> failed
    );

    internal sealed record Succeeded(
        string Code,
        AutomationOutputSelection OutputSelection,
        DateTime NextAvailableAtUtc
    ) : AutomationNodeExecution
    {
        internal override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Failed, TResult> failed
        ) => succeeded(this);
    }

    internal sealed record Failed(string Code, AutomationDiagnostic? Diagnostic = null)
        : AutomationNodeExecution
    {
        internal override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Failed, TResult> failed
        ) => failed(this);
    }
}
