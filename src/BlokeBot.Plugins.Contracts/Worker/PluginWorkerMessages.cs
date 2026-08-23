using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "message")]
[JsonDerivedType(typeof(PluginWorkerMessage.WorkerHello), "worker-hello")]
[JsonDerivedType(typeof(PluginWorkerMessage.HostHandshake), "host-handshake")]
[JsonDerivedType(typeof(PluginWorkerMessage.HandshakeAccepted), "handshake-accepted")]
[JsonDerivedType(typeof(PluginWorkerMessage.HandshakeRejected), "handshake-rejected")]
[JsonDerivedType(typeof(PluginWorkerMessage.Prepare), "prepare")]
[JsonDerivedType(typeof(PluginWorkerMessage.Invoke), "invoke")]
[JsonDerivedType(typeof(PluginWorkerMessage.Cancel), "cancel")]
[JsonDerivedType(typeof(PluginWorkerMessage.HostCallRequested), "host-call-requested")]
[JsonDerivedType(typeof(PluginWorkerMessage.HostCallCompleted), "host-call-completed")]
[JsonDerivedType(typeof(PluginWorkerMessage.InvocationCompleted), "invocation-completed")]
[JsonDerivedType(typeof(PluginWorkerMessage.InvocationCancelled), "invocation-cancelled")]
[JsonDerivedType(typeof(PluginWorkerMessage.InvocationRejected), "invocation-rejected")]
[JsonDerivedType(typeof(PluginWorkerMessage.Diagnostics), "diagnostics")]
[JsonDerivedType(typeof(PluginWorkerMessage.Stop), "stop")]
[JsonDerivedType(typeof(PluginWorkerMessage.Stopped), "stopped")]
[JsonDerivedType(typeof(PluginWorkerMessage.ProtocolRejected), "protocol-rejected")]
public abstract record PluginWorkerMessage
{
    private PluginWorkerMessage() { }

    public sealed record WorkerHello(
        PluginWorkerCompatibilityDescriptor Compatibility,
        PluginEngineDescriptor Engine,
        PluginRuntimeIdentifier RuntimeIdentifier
    ) : PluginWorkerMessage;

    public sealed record HostHandshake(
        PluginWorkerCompatibilityDescriptor Compatibility,
        PluginEngineDescriptor Engine,
        PluginWorkerMode Mode,
        PluginWorkerPackageDescriptor Package
    ) : PluginWorkerMessage;

    public sealed record HandshakeAccepted(
        PluginWorkerMode Mode,
        PluginWorkerPackageDescriptor Package
    ) : PluginWorkerMessage;

    public sealed record HandshakeRejected(PluginWorkerHandshakeFailure Failure)
        : PluginWorkerMessage;

    public sealed record Prepare(
        PluginWorkerInvocationIdentity Identity,
        PluginPreparationInvocation Invocation
    ) : PluginWorkerMessage;

    public sealed record Invoke(
        PluginWorkerInvocationIdentity Identity,
        PluginLiveInvocation Invocation
    ) : PluginWorkerMessage;

    public sealed record Cancel(
        PluginWorkerInvocationIdentity Identity,
        PluginCancellationReason Reason
    ) : PluginWorkerMessage;

    public sealed record HostCallRequested(
        PluginWorkerInvocationId InvocationId,
        PluginWorkerCancellationId CancellationId,
        PluginHostCall Call
    ) : PluginWorkerMessage;

    public sealed record HostCallCompleted(
        PluginWorkerInvocationId InvocationId,
        PluginWorkerCancellationId CancellationId,
        PluginHostCallCompletion Completion
    ) : PluginWorkerMessage;

    public sealed record InvocationCompleted(
        PluginWorkerInvocationId InvocationId,
        PluginWorkerInvocationOutcome Outcome,
        PluginWorkerInvocationMetrics Metrics
    ) : PluginWorkerMessage;

    public sealed record InvocationCancelled(
        PluginWorkerInvocationId InvocationId,
        PluginCancellationReason Reason,
        PluginWorkerInvocationMetrics Metrics
    ) : PluginWorkerMessage;

    public sealed record InvocationRejected(
        PluginWorkerInvocationId InvocationId,
        PluginWorkerFailure Failure
    ) : PluginWorkerMessage;

    public sealed record Diagnostics(
        PluginWorkerInvocationId InvocationId,
        ImmutableArray<PluginWorkerDiagnostic> Items
    ) : PluginWorkerMessage;

    public sealed record Stop : PluginWorkerMessage;

    public sealed record Stopped : PluginWorkerMessage;

    public sealed record ProtocolRejected(PluginWorkerFailure Failure) : PluginWorkerMessage;
}

public enum PluginWorkerHandshakeFailureCode
{
    ProtocolSkew,
    EngineMismatch,
    ApiMismatch,
    TargetMismatch,
    PackageMismatch,
    PackageUnavailable,
}

public sealed record PluginWorkerHandshakeFailure(
    PluginWorkerHandshakeFailureCode Code,
    string Subject
);

public enum PluginWorkerFailureCode
{
    MalformedFrame,
    FrameTooLarge,
    HandshakeRequired,
    ProtocolViolation,
    StagingLiveWorkRejected,
    InvocationLimitExceeded,
    DeadlineExceeded,
    InvalidValue,
    UnknownModule,
    UnknownOperation,
    EngineFailure,
    OutputLimitExceeded,
    DiagnosticLimitExceeded,
    WorkerExited,
    WorkerTerminated,
}

public sealed record PluginWorkerFailure(PluginWorkerFailureCode Code, string SafeMessage);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PluginWorkerInvocationOutcome.Returned), "returned")]
[JsonDerivedType(typeof(PluginWorkerInvocationOutcome.Failed), "failed")]
[JsonDerivedType(typeof(PluginWorkerInvocationOutcome.Cancelled), "cancelled")]
public abstract record PluginWorkerInvocationOutcome
{
    private PluginWorkerInvocationOutcome() { }

    public sealed record Returned(PluginValue Value) : PluginWorkerInvocationOutcome;

    public sealed record Failed(PluginWorkerFailure Failure) : PluginWorkerInvocationOutcome;

    public sealed record Cancelled(PluginCancellationReason Reason, bool WorkerTerminated)
        : PluginWorkerInvocationOutcome;
}

public sealed record PluginWorkerInvocationMetrics(
    int ResumeCount,
    int HostCallCount,
    int DiagnosticCount,
    long OutputBytes,
    long DurationMilliseconds
)
{
    public static PluginWorkerInvocationMetrics Empty { get; } = new(0, 0, 0, 0, 0);
}

public enum PluginWorkerDiagnosticLevel
{
    Trace,
    Information,
    Warning,
    Error,
}

public sealed record PluginWorkerDiagnostic(PluginWorkerDiagnosticLevel Level, string Message);
