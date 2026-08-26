using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

internal sealed class PublishedPluginExampleHost(bool delayFirstCall) : IPluginHostCallDispatcher
{
    private readonly bool _delayFirstCall = delayFirstCall;
    private int _callCount;

    internal TaskCompletionSource EffectCompleted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource ReleaseLateResult { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource LateDispatchCompleted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool ExternalEffectCompleted { get; private set; }

    public async ValueTask<PluginHostCallOutcome> DispatchAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var module = PluginStandardHostModules.All.FirstOrDefault(candidate =>
            candidate.Id == call.Module
        );
        if (
            module is null
            || PluginHostCallValidator.ValidateCall(call, module)
                is PluginHostCallValidationOutcome.Invalid
        )
        {
            return new PluginHostCallOutcome.Failed(
                new(PluginHostFailureCode.InvalidArguments, "Example host call was invalid.")
            );
        }

        var outcome = Outcome(call);
        if (_delayFirstCall && Interlocked.Increment(ref _callCount) == 1)
        {
            ExternalEffectCompleted = true;
            _ = EffectCompleted.TrySetResult();
            await ReleaseLateResult.Task;
            _ = LateDispatchCompleted.TrySetResult();
        }

        var operation = module.Operations.Single(candidate => candidate.Id == call.Operation);
        return
            PluginHostCallValidator.ValidateOutcome(outcome, operation)
            is PluginHostCallValidationOutcome.Valid
            ? outcome
            : new PluginHostCallOutcome.Failed(
                new(PluginHostFailureCode.Unavailable, "Example host result was invalid.")
            );
    }

    private static PluginHostCallOutcome Outcome(PluginHostCall call) =>
        (call.Module.Value, call.Operation.Value) switch
        {
            ("points", "add") => Returned(new PluginValue.String("7")),
            ("schedules", "once" or "recurring") => Returned(
                new PluginValue.String("00000000-0000-0000-0000-000000000001")
            ),
            ("storage", "execute") => Returned(new PluginValue.Number(1)),
            ("storage", "query") => Returned(
                new PluginValue.Array([
                    new PluginValue.Map([
                        new("message", new PluginValue.String("local fixture row")),
                    ]),
                ])
            ),
            ("http", "send") => Returned(
                new PluginValue.Map([
                    new("status", new PluginValue.Number(200)),
                    new("body", new PluginValue.String("local fixture response")),
                ])
            ),
            _ => Returned(new PluginValue.Nil()),
        };

    private static PluginHostCallOutcome Returned(PluginValue value) =>
        new PluginHostCallOutcome.Returned(value);
}
