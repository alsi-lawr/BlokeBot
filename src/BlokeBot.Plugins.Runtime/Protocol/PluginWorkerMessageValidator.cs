using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal abstract record PluginWorkerMessageValidationOutcome
{
    private PluginWorkerMessageValidationOutcome() { }

    internal sealed record Valid : PluginWorkerMessageValidationOutcome;

    internal sealed record Rejected(PluginWorkerFailure Failure)
        : PluginWorkerMessageValidationOutcome;
}

internal static class PluginWorkerMessageValidator
{
    private static readonly PluginWorkerMessageValidationOutcome.Valid _valid = new();

    internal static PluginWorkerMessageValidationOutcome Validate(PluginWorkerMessage message) =>
        message switch
        {
            PluginWorkerMessage.Prepare prepare => Validate(prepare.Invocation.Input),
            PluginWorkerMessage.Invoke invoke => Validate(invoke.Invocation.Input),
            PluginWorkerMessage.HostCallRequested requested => Validate(requested.Call.Arguments),
            PluginWorkerMessage.HostCallCompleted
            {
                Completion.Outcome: PluginHostCallOutcome.Returned returned,
            } => Validate(returned.Value),
            PluginWorkerMessage.InvocationCompleted
            {
                Outcome: PluginWorkerInvocationOutcome.Returned returned,
            } => Validate(returned.Value),
            _ => _valid,
        };

    private static PluginWorkerMessageValidationOutcome Validate(IEnumerable<PluginValue> values)
    {
        foreach (var value in values)
        {
            if (Validate(value) is PluginWorkerMessageValidationOutcome.Rejected rejected)
            {
                return rejected;
            }
        }

        return _valid;
    }

    private static PluginWorkerMessageValidationOutcome Validate(PluginValue value) =>
        PluginValueValidator.Validate(value) is PluginValueValidationOutcome.Valid
            ? _valid
            : new PluginWorkerMessageValidationOutcome.Rejected(
                new(
                    PluginWorkerFailureCode.InvalidValue,
                    "Worker message contains an invalid plugin value."
                )
            );
}
