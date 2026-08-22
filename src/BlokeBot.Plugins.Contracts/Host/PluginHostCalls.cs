using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginHostCall(
    PluginHostCallId CallId,
    PluginCoroutineId CoroutineId,
    PluginHostModuleId Module,
    PluginHostOperationId Operation,
    PluginInvocationContext Context,
    ImmutableArray<PluginValue> Arguments
);

public enum PluginHostFailureCode
{
    InvalidArguments,
    ContextNotPermitted,
    NotFound,
    Conflict,
    Unavailable,
    ProviderRejected,
}

public sealed record PluginHostFailure(PluginHostFailureCode Code, string SafeMessage);

public enum PluginCancellationReason
{
    CallerRequested,
    PluginDisabled,
    PluginUpdating,
    WorkerStopping,
    DeadlineExceeded,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PluginHostCallOutcome.Returned), "returned")]
[JsonDerivedType(typeof(PluginHostCallOutcome.Failed), "failed")]
[JsonDerivedType(typeof(PluginHostCallOutcome.Cancelled), "cancelled")]
public abstract record PluginHostCallOutcome
{
    private PluginHostCallOutcome() { }

    public sealed record Returned(PluginValue Value) : PluginHostCallOutcome;

    public sealed record Failed(PluginHostFailure Failure) : PluginHostCallOutcome;

    public sealed record Cancelled(PluginCancellationReason Reason) : PluginHostCallOutcome;
}

public sealed record PluginHostCallCompletion(
    PluginHostCallId CallId,
    PluginCoroutineId CoroutineId,
    PluginHostCallOutcome Outcome
);

public sealed record PluginHostCallCancellation(
    PluginHostCallId CallId,
    PluginCoroutineId CoroutineId,
    PluginCancellationReason Reason
);

public interface IPluginHostModule
{
    PluginHostModuleDescriptor Descriptor { get; }

    ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    );
}
