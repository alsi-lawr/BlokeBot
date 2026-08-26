using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

public static class PublishedPluginExampleHarness
{
    public static async ValueTask<PublishedPluginExampleHarnessOutcome> RunAsync(
        PublishedPluginExampleHarnessOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<PublishedPluginExampleFailure>();
        IReadOnlyList<PublishedPluginExample> examples;
        try
        {
            examples = await PublishedPluginExampleSourceLoader.LoadAsync(
                options.SourceRoot,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new PublishedPluginExampleHarnessOutcome.Failed([
                new(
                    PublishedPluginExampleFailureCode.SourceInvalid,
                    Path.GetFileName(options.SourceRoot),
                    exception.Message
                ),
            ]);
        }

        if (!File.Exists(options.WorkerExecutable.Path))
        {
            return new PublishedPluginExampleHarnessOutcome.Failed([
                new(
                    PublishedPluginExampleFailureCode.WorkerUnavailable,
                    "$worker",
                    options.WorkerExecutable.Path
                ),
            ]);
        }

        if (!PluginRuntimeIdentifierResolver.TryResolveCurrent(out var currentRuntimeIdentifier))
        {
            return new PublishedPluginExampleHarnessOutcome.Failed([
                new(
                    PublishedPluginExampleFailureCode.WorkerUnavailable,
                    "$worker",
                    "Current runtime identifier is unsupported."
                ),
            ]);
        }

        var observations = new List<PublishedPluginExampleObservation>();
        foreach (var example in examples)
        {
            var observation = await RunExampleAsync(
                example,
                options.WorkerExecutable,
                currentRuntimeIdentifier,
                failures,
                cancellationToken
            );
            if (observation is not null)
            {
                observations.Add(observation);
            }
        }

        return failures.Count == 0
            ? new PublishedPluginExampleHarnessOutcome.Passed([.. observations])
            : new PublishedPluginExampleHarnessOutcome.Failed([.. failures]);
    }

    private static async ValueTask<PublishedPluginExampleObservation?> RunExampleAsync(
        PublishedPluginExample example,
        PluginWorkerExecutable workerExecutable,
        PluginRuntimeIdentifier currentRuntimeIdentifier,
        List<PublishedPluginExampleFailure> failures,
        CancellationToken cancellationToken
    )
    {
        var validatedRuntimeIdentifiers = ImmutableArray.CreateBuilder<PluginRuntimeIdentifier>();
        foreach (var runtimeIdentifier in PluginAuthoringContract.Current.RuntimeIdentifiers)
        {
            if (
                PluginPackageValidator.Validate(
                    example.Package,
                    PluginAuthoringContract.Current.Target(runtimeIdentifier)
                ) is PluginPackageValidationOutcome.Accepted
            )
            {
                validatedRuntimeIdentifiers.Add(runtimeIdentifier);
            }
            else
            {
                failures.Add(
                    new(
                        PublishedPluginExampleFailureCode.PackageRejected,
                        example.Name,
                        runtimeIdentifier.ToString()
                    )
                );
            }
        }

        if (
            validatedRuntimeIdentifiers.Count
            != PluginAuthoringContract.Current.RuntimeIdentifiers.Length
        )
        {
            return null;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-published-example-{Guid.NewGuid():N}"
        );
        _ = Directory.CreateDirectory(root);
        try
        {
            var materialized = await PluginWorkerPackageMaterializer.MaterializeAsync(
                example.Package,
                PluginAuthoringContract.Current.Target(currentRuntimeIdentifier),
                Path.Combine(root, "package"),
                cancellationToken
            );
            if (materialized is not PluginPackageMaterializationOutcome.Prepared prepared)
            {
                failures.Add(
                    new(
                        PublishedPluginExampleFailureCode.PackageRejected,
                        example.Name,
                        currentRuntimeIdentifier.ToString()
                    )
                );
                return null;
            }

            var executed = ImmutableArray.CreateBuilder<string>();
            var externalEffectRemainedCompleted = false;
            var lateHostResultDiscarded = false;
            foreach (var scenario in example.Scenarios)
            {
                var execution = await PublishedPluginExampleScenarioRunner.RunAsync(
                    prepared.Package,
                    scenario,
                    workerExecutable,
                    Path.Combine(root, $"state-{executed.Count}"),
                    cancellationToken
                );
                if (execution.Failure is not null)
                {
                    failures.Add(execution.Failure with { Example = example.Name });
                    continue;
                }

                executed.Add(scenario.Name);
                externalEffectRemainedCompleted |= execution.ExternalEffectRemainedCompleted;
                lateHostResultDiscarded |= execution.LateHostResultDiscarded;
            }

            return new(
                example.Name,
                validatedRuntimeIdentifiers.ToImmutable(),
                currentRuntimeIdentifier,
                executed.ToImmutable(),
                externalEffectRemainedCompleted,
                lateHostResultDiscarded
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
