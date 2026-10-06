namespace BlokeBot.Core.Features.Automations;

internal static class AutomationDelayDurationBinding
{
    internal static AutomationConfigurationFieldId LiteralField { get; } =
        new("duration-milliseconds");
    internal static AutomationConfigurationFieldId ValueField { get; } =
        new("duration-value-milliseconds");
    internal static AutomationPortId Port { get; } = new("duration");
    internal const long MaximumMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;

    internal static bool IsInput(
        AutomationDefinitionDescriptor definition,
        AutomationPortMetadata input
    ) =>
        definition.Id == AutomationDefinitionIds.DelayControl
        && input.Id == Port
        && input.BindingFieldId == ValueField;

    internal static Admission Admit(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding> bindings,
        int incoming
    ) =>
        !bindings.TryGetValue(ValueField, out var binding)
            ? incoming == 0
                ? new Admission.LegacyLiteral()
                : new Admission.Invalid()
            : binding.Mode switch
            {
                AutomationInputBindingMode.Fixed when incoming == 0 => new Admission.Bound(binding),
                AutomationInputBindingMode.Expression
                    when incoming == 0 && binding.Expression is not null => new Admission.Bound(
                    binding
                ),
                AutomationInputBindingMode.Connected when incoming == 1 => new Admission.Bound(
                    binding
                ),
                _ => new Admission.Invalid(),
            };

    internal static Duration Resolve(
        DelayControlConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs
    ) =>
        !inputs.TryGetValue(ValueField, out var value)
            ? new Duration.LegacyLiteral(configuration.Duration)
        : value.Value is AutomationValue.Number number
            ? new Duration.ResolvedMilliseconds(number.Value)
        : new Duration.Invalid();

    internal abstract record Admission
    {
        private Admission() { }

        internal sealed record LegacyLiteral : Admission;

        internal sealed record Bound(AutomationInputBinding Binding) : Admission;

        internal sealed record Invalid : Admission;
    }

    internal abstract record Duration
    {
        private Duration() { }

        internal sealed record LegacyLiteral(TimeSpan Value) : Duration;

        internal sealed record ResolvedMilliseconds(decimal Value) : Duration;

        internal sealed record Invalid : Duration;
    }
}
