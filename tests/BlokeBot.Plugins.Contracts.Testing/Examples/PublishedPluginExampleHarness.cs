using System.Collections.Immutable;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Contracts.Testing;

public static class PublishedPluginExampleHarness
{
    public static async ValueTask<PublishedPluginExampleValidationOutcome> ValidateAsync(
        PublishedPluginExampleValidationOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var loaded = await PublishedPluginExampleSourceLoader.LoadForValidationAsync(
            options.SourceRoot,
            cancellationToken
        );
        if (loaded is PublishedPluginExampleSourceLoadOutcome.Rejected rejected)
        {
            return new PublishedPluginExampleValidationOutcome.Rejected(rejected.Failures);
        }
        var examples = ((PublishedPluginExampleSourceLoadOutcome.Loaded)loaded).Examples;

        var failures = new List<PublishedPluginExampleFailure>();
        var observations = new List<PublishedPluginExampleValidationObservation>();
        foreach (var example in examples)
        {
            var validated = ValidateExample(example, failures);
            if (validated.Length == PluginAuthoringContract.Current.RuntimeIdentifiers.Length)
            {
                observations.Add(new(example.Name, validated));
            }
        }

        return failures.Count == 0
            ? new PublishedPluginExampleValidationOutcome.Accepted([.. observations])
            : new PublishedPluginExampleValidationOutcome.Rejected([.. failures]);
    }

    public static async ValueTask<PublishedPluginExampleHarnessOutcome> RunAsync(
        PublishedPluginExampleHarnessOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var loaded = await PublishedPluginExampleSourceLoader.LoadForTestsAsync(
            options.SourceRoot,
            cancellationToken
        );
        if (loaded is PublishedPluginExampleSourceLoadOutcome.Rejected rejected)
        {
            return new PublishedPluginExampleHarnessOutcome.Failed(rejected.Failures);
        }
        var examples = ((PublishedPluginExampleSourceLoadOutcome.Loaded)loaded).Examples;

        var failures = new List<PublishedPluginExampleFailure>();
        var validated = examples.ToDictionary(
            example => example,
            example => ValidateExample(example, failures)
        );
        if (failures.Count > 0)
        {
            return new PublishedPluginExampleHarnessOutcome.Failed([.. failures]);
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
                validated[example],
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

    private static ImmutableArray<PluginRuntimeIdentifier> ValidateExample(
        PublishedPluginExample example,
        List<PublishedPluginExampleFailure> failures
    )
    {
        var validated = ImmutableArray.CreateBuilder<PluginRuntimeIdentifier>();
        foreach (var runtimeIdentifier in PluginAuthoringContract.Current.RuntimeIdentifiers)
        {
            var outcome = PluginPackageValidator.Validate(
                example.Package,
                PluginAuthoringContract.Current.Target(runtimeIdentifier)
            );
            if (outcome is PluginPackageValidationOutcome.Accepted)
            {
                validated.Add(runtimeIdentifier);
                continue;
            }

            failures.Add(
                new(
                    PublishedPluginExampleFailureCode.PackageRejected,
                    example.Name,
                    PackageFailureSubject(
                        runtimeIdentifier,
                        (PluginPackageValidationOutcome.Rejected)outcome
                    )
                )
            );
        }

        return validated.ToImmutable();
    }

    private static async ValueTask<PublishedPluginExampleObservation?> RunExampleAsync(
        PublishedPluginExample example,
        ImmutableArray<PluginRuntimeIdentifier> validatedRuntimeIdentifiers,
        PluginWorkerExecutable workerExecutable,
        PluginRuntimeIdentifier currentRuntimeIdentifier,
        List<PublishedPluginExampleFailure> failures,
        CancellationToken cancellationToken
    )
    {
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
            var updateMigrationFaulted = false;
            var oldRuntimeRemainedStopped = false;
            var updateRecoveryRemainedFaulted = false;
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
                updateMigrationFaulted |= execution.UpdateMigrationFaulted;
                oldRuntimeRemainedStopped |= execution.OldRuntimeRemainedStopped;
                updateRecoveryRemainedFaulted |= execution.UpdateRecoveryRemainedFaulted;
            }

            return new(
                example.Name,
                validatedRuntimeIdentifiers,
                currentRuntimeIdentifier,
                executed.ToImmutable(),
                externalEffectRemainedCompleted,
                lateHostResultDiscarded,
                updateMigrationFaulted,
                oldRuntimeRemainedStopped,
                updateRecoveryRemainedFaulted
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

    private static string PackageFailureSubject(
        PluginRuntimeIdentifier runtimeIdentifier,
        PluginPackageValidationOutcome.Rejected rejected
    ) => $"{runtimeIdentifier}: {string.Join(", ", rejected.Errors.Select(PackageErrorCode))}";

    private static string PackageErrorCode(PluginPackageError error) =>
        error switch
        {
            PluginPackageError.Entry entry => entry.Code.ToString(),
            PluginPackageError.Manifest manifest => string.Join(
                "+",
                manifest.Errors.Select(candidate => candidate.Code)
            ),
            _ => throw new InvalidOperationException("Unknown package validation error."),
        };
}
