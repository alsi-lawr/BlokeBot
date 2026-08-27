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

    internal List<string> Calls { get; } = [];

    public async ValueTask<PluginHostCallOutcome> DispatchAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => await DispatchCoreAsync(identity: null, call, cancellationToken);

    public async ValueTask<PluginHostCallOutcome> DispatchAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => await DispatchCoreAsync(identity, call, cancellationToken);

    private async ValueTask<PluginHostCallOutcome> DispatchCoreAsync(
        PluginWorkerInvocationIdentity? identity,
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

        Calls.Add($"{call.Module.Value}.{call.Operation.Value}");
        var outcome = Outcome(identity, call);
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

    private static PluginHostCallOutcome Outcome(
        PluginWorkerInvocationIdentity? identity,
        PluginHostCall call
    ) =>
        (call.Module.Value, call.Operation.Value) switch
        {
            ("context", "current") when identity is not null => Returned(Context(identity)),
            ("settings", "installation") => Returned(
                new PluginValue.Map([
                    new(
                        "metadata-endpoint",
                        new PluginValue.String("https://example.invalid/metadata")
                    ),
                    new("metadata-token", new PluginValue.String("local-fixture-token")),
                ])
            ),
            ("settings", "feature") => Returned(
                new PluginValue.Map([new("publish-interval", new PluginValue.Number(300))])
            ),
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
                    new("kind", new PluginValue.String("response")),
                    new("status", new PluginValue.Number(200)),
                    new("headers", new PluginValue.Map([])),
                    new("bodyBase64", new PluginValue.String("")),
                ])
            ),
            _ => Returned(new PluginValue.Nil()),
        };

    private static PluginValue.Map Context(PluginWorkerInvocationIdentity identity)
    {
        var properties = new List<PluginValueProperty>
        {
            new(
                "kind",
                new PluginValue.String(identity.Context.Kind.ToString().ToLowerInvariant())
            ),
            new("pluginId", new PluginValue.String(identity.Plugin.PluginId.Value)),
            new(
                "pluginVersion",
                new PluginValue.String(identity.Plugin.Release.DeclaredVersion.Value)
            ),
            new("pluginTag", new PluginValue.String(identity.Plugin.Release.Tag.Value)),
        };
        if (
            identity.Context
            is PluginInvocationContext.Channel
                or PluginInvocationContext.Automation
                or PluginInvocationContext.Page
        )
        {
            properties.Add(new("hostId", new PluginValue.Number(identity.Host.Value)));
            properties.Add(new("featureId", new PluginValue.String(identity.Feature.Value)));
        }
        return new([.. properties]);
    }

    private static PluginHostCallOutcome Returned(PluginValue value) =>
        new PluginHostCallOutcome.Returned(value);
}
