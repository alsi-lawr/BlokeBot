using System.Collections.Immutable;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

public enum PublishedPluginExampleInvocationKind
{
    Lifecycle,
    Migration,
    Command,
    Event,
    Schedule,
    HostAction,
    Storage,
    Page,
    Automation,
}

public enum PublishedPluginExampleExpectation
{
    Returned,
    Failed,
    Cancelled,
    WorkerExited,
    MigrationFailed,
}

public sealed record PublishedPluginExampleScenario(
    string Name,
    PluginWorkerMode WorkerMode,
    PublishedPluginExampleInvocationKind InvocationKind,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PublishedPluginExampleExpectation Expectation,
    PluginValue Input,
    ImmutableArray<string> ExpectedHostCalls
);

public sealed record PublishedPluginExample(
    string Name,
    string SourceDirectory,
    IReadOnlyList<PluginPackageEntry> Package,
    ImmutableArray<PublishedPluginExampleScenario> Scenarios
);

public enum PublishedPluginExampleFailureCode
{
    SourceInvalid,
    TestMetadataMissing,
    TestMetadataMalformed,
    TestMetadataInvalid,
    PackageRejected,
    WorkerUnavailable,
    WorkerStartRejected,
    InvocationExpectationMismatch,
    CancellationFixtureIncomplete,
}

public sealed record PublishedPluginExampleFailure(
    PublishedPluginExampleFailureCode Code,
    string Example,
    string Subject
);

public abstract record PublishedPluginExampleSourceLoadOutcome
{
    private PublishedPluginExampleSourceLoadOutcome() { }

    public sealed record Loaded(ImmutableArray<PublishedPluginExample> Examples)
        : PublishedPluginExampleSourceLoadOutcome;

    public sealed record Rejected(ImmutableArray<PublishedPluginExampleFailure> Failures)
        : PublishedPluginExampleSourceLoadOutcome;
}

public sealed record PublishedPluginExampleValidationObservation(
    string Example,
    ImmutableArray<PluginRuntimeIdentifier> RuntimeIdentifiers
);

public abstract record PublishedPluginExampleValidationOutcome
{
    private PublishedPluginExampleValidationOutcome() { }

    public sealed record Accepted(
        ImmutableArray<PublishedPluginExampleValidationObservation> Observations
    ) : PublishedPluginExampleValidationOutcome;

    public sealed record Rejected(ImmutableArray<PublishedPluginExampleFailure> Failures)
        : PublishedPluginExampleValidationOutcome;
}

public sealed record PublishedPluginExampleValidationOptions(string SourceRoot);

public sealed record PublishedPluginExampleObservation(
    string Example,
    ImmutableArray<PluginRuntimeIdentifier> ValidatedRuntimeIdentifiers,
    PluginRuntimeIdentifier ExecutedRuntimeIdentifier,
    ImmutableArray<string> ExecutedScenarios,
    bool ExternalEffectRemainedCompleted,
    bool LateHostResultDiscarded,
    bool UpdateMigrationFaulted,
    bool OldRuntimeRemainedStopped,
    bool UpdateRecoveryRemainedFaulted
);

public abstract record PublishedPluginExampleHarnessOutcome
{
    private PublishedPluginExampleHarnessOutcome() { }

    public sealed record Passed(ImmutableArray<PublishedPluginExampleObservation> Observations)
        : PublishedPluginExampleHarnessOutcome;

    public sealed record Failed(ImmutableArray<PublishedPluginExampleFailure> Failures)
        : PublishedPluginExampleHarnessOutcome;
}

public sealed record PublishedPluginExampleHarnessOptions(
    string SourceRoot,
    PluginWorkerExecutable WorkerExecutable
);
