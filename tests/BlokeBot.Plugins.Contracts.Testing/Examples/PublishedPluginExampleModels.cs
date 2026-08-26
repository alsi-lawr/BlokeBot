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
}

public sealed record PublishedPluginExampleScenario(
    string Name,
    PluginWorkerMode WorkerMode,
    PublishedPluginExampleInvocationKind InvocationKind,
    PluginLuaModuleId Module,
    PluginHostOperationId Operation,
    PublishedPluginExampleExpectation Expectation
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

public sealed record PublishedPluginExampleObservation(
    string Example,
    ImmutableArray<PluginRuntimeIdentifier> ValidatedRuntimeIdentifiers,
    PluginRuntimeIdentifier ExecutedRuntimeIdentifier,
    ImmutableArray<string> ExecutedScenarios,
    bool ExternalEffectRemainedCompleted,
    bool LateHostResultDiscarded
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
