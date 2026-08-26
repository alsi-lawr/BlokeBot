using System.Diagnostics;
using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Plugins.Contracts.Testing;

internal static class PublishedPluginExampleScenarioRunner
{
    internal static async ValueTask<PublishedPluginExampleScenarioExecution> RunAsync(
        PreparedPluginWorkerPackage package,
        PublishedPluginExampleScenario scenario,
        PluginWorkerExecutable workerExecutable,
        string stateRoot,
        CancellationToken cancellationToken
    )
    {
        var cancellationFixture =
            scenario.Expectation == PublishedPluginExampleExpectation.Cancelled;
        var host = new PublishedPluginExampleHost(cancellationFixture);
        var started = await PluginWorkerClient.StartAsync(
            new(
                package,
                stateRoot,
                scenario.WorkerMode,
                host,
                NullLogger<PluginWorkerClient>.Instance,
                workerExecutable
            ),
            cancellationToken
        );
        if (started is not PluginWorkerStartOutcome.Started worker)
        {
            var subject = started switch
            {
                PluginWorkerStartOutcome.Rejected rejected => rejected.Failure.Code.ToString(),
                PluginWorkerStartOutcome.Failed failed => failed.Failure.Code.ToString(),
                _ => throw new UnreachableException("Unknown plugin worker start outcome."),
            };
            return PublishedPluginExampleScenarioExecution.Failed(
                PublishedPluginExampleFailureCode.WorkerStartRejected,
                scenario.Name,
                subject
            );
        }

        await using var client = worker.Client;
        var identity = PublishedPluginExampleInvocationFactory.Identity(
            package,
            scenario.InvocationKind
        );
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var invocation = InvokeAsync(client, identity, scenario, callerCancellation.Token).AsTask();
        if (cancellationFixture)
        {
            await host.EffectCompleted.Task.WaitAsync(cancellationToken);
            callerCancellation.Cancel();
        }

        var result = await invocation;
        if (!MatchesExpectation(result.Outcome, scenario.Expectation))
        {
            return PublishedPluginExampleScenarioExecution.Failed(
                PublishedPluginExampleFailureCode.InvocationExpectationMismatch,
                scenario.Name,
                result.Outcome.GetType().Name
            );
        }

        if (!cancellationFixture)
        {
            return PublishedPluginExampleScenarioExecution.Passed();
        }

        _ = host.ReleaseLateResult.TrySetResult();
        await host.LateDispatchCompleted.Task.WaitAsync(cancellationToken);
        return host.ExternalEffectCompleted
            ? PublishedPluginExampleScenarioExecution.Passed(
                externalEffectRemainedCompleted: true,
                lateHostResultDiscarded: true
            )
            : PublishedPluginExampleScenarioExecution.Failed(
                PublishedPluginExampleFailureCode.CancellationFixtureIncomplete,
                scenario.Name,
                "The completed external effect was lost."
            );
    }

    private static ValueTask<PluginWorkerInvocationResult> InvokeAsync(
        PluginWorkerClient client,
        PluginWorkerInvocationIdentity identity,
        PublishedPluginExampleScenario scenario,
        CancellationToken cancellationToken
    ) =>
        scenario.WorkerMode == PluginWorkerMode.Staging
            ? client.PrepareAsync(
                identity,
                new(scenario.Module, scenario.Operation, new PluginValue.Nil()),
                cancellationToken
            )
            : client.InvokeAsync(
                identity,
                PublishedPluginExampleInvocationFactory.LiveInvocation(scenario),
                cancellationToken
            );

    private static bool MatchesExpectation(
        PluginWorkerInvocationOutcome outcome,
        PublishedPluginExampleExpectation expectation
    ) =>
        (outcome, expectation) switch
        {
            (PluginWorkerInvocationOutcome.Returned, PublishedPluginExampleExpectation.Returned) =>
                true,
            (
                PluginWorkerInvocationOutcome.Failed
                {
                    Failure.Code: PluginWorkerFailureCode.EngineFailure,
                },
                PublishedPluginExampleExpectation.Failed
            ) => true,
            (
                PluginWorkerInvocationOutcome.Cancelled
                {
                    Reason: PluginCancellationReason.CallerRequested,
                },
                PublishedPluginExampleExpectation.Cancelled
            ) => true,
            (
                PluginWorkerInvocationOutcome.Failed
                {
                    Failure.Code: PluginWorkerFailureCode.WorkerExited,
                },
                PublishedPluginExampleExpectation.WorkerExited
            ) => true,
            _ => false,
        };
}

internal sealed record PublishedPluginExampleScenarioExecution(
    PublishedPluginExampleFailure? Failure,
    bool ExternalEffectRemainedCompleted,
    bool LateHostResultDiscarded
)
{
    internal static PublishedPluginExampleScenarioExecution Passed(
        bool externalEffectRemainedCompleted = false,
        bool lateHostResultDiscarded = false
    ) => new(null, externalEffectRemainedCompleted, lateHostResultDiscarded);

    internal static PublishedPluginExampleScenarioExecution Failed(
        PublishedPluginExampleFailureCode code,
        string subject,
        string detail
    ) => new(new(code, "$pending", $"{subject}: {detail}"), false, false);
}
