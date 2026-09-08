namespace BlokeBot.Core.Features.Automations;

internal static class AutomationNodeEvaluation
{
    internal static AutomationNodeExecution Delay(
        DelayControlConfiguration configuration,
        DateTime now
    ) =>
        configuration.Duration <= DateTime.MaxValue - now
            ? new AutomationNodeExecution.Succeeded("delayed", null, now + configuration.Duration)
            : new AutomationNodeExecution.Failed("delay-unrepresentable");

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
