namespace BlokeBot.Core.Features.Automations;

internal static class AutomationNodeEvaluation
{
    internal static AutomationNodeExecution Delay(
        DelayControlConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        DateTime now
    )
    {
        TimeSpan? duration = AutomationDelayDurationBinding.Resolve(configuration, inputs) switch
        {
            AutomationDelayDurationBinding.Duration.LegacyLiteral literal => literal.Value,
            AutomationDelayDurationBinding.Duration.ResolvedMilliseconds milliseconds
                when milliseconds.Value > 0
                    && milliseconds.Value <= AutomationDelayDurationBinding.MaximumMilliseconds
                    && decimal.Truncate(milliseconds.Value) == milliseconds.Value =>
                TimeSpan.FromTicks((long)milliseconds.Value * TimeSpan.TicksPerMillisecond),
            _ => null,
        };
        return duration is not { } wait
                ? new AutomationNodeExecution.Failed("delay-invalid-duration")
            : wait <= DateTime.MaxValue - now
                ? new AutomationNodeExecution.Succeeded("delayed", null, now + wait)
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
                result ? "yes" : "no",
                now
            ),
            _ => new AutomationNodeExecution.Failed("condition-invalid"),
        };
}

internal abstract record AutomationNodeExecution
{
    private AutomationNodeExecution() { }

    internal sealed record Succeeded(string Code, string? OutputPort, DateTime NextAvailableAtUtc)
        : AutomationNodeExecution;

    internal sealed record Failed(string Code) : AutomationNodeExecution;
}
