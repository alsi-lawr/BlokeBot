using System.Runtime.InteropServices;

namespace BlokeBot.DatabaseWorkloads;

internal sealed partial class DatabaseBaselineRunner(
    WorkloadProtocol protocol,
    string protocolDigest,
    WorkloadDatabase database
) : IAsyncDisposable
{
    private static readonly DateTime _epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<WorkloadId, WorkloadDefinition> _definitions =
        protocol.Workloads.ToDictionary(static definition => definition.Id);

    internal async Task<BaselineResult> RunAsync(CancellationToken cancellationToken)
    {
        ThreadPool.GetMinThreads(out var minimumWorkerThreads, out var minimumIoThreads);
        _ = ThreadPool.SetMinThreads(
            Math.Max(minimumWorkerThreads, protocol.Concurrency.Writers * 4),
            minimumIoThreads
        );

        var measurements = protocol.Workloads.ToDictionary(
            static definition => definition.Id,
            static _ => new WorkloadMeasurements()
        );
        Dictionary<string, long>? expectedOutcomes = null;
        IReadOnlyList<QueryPlanResult> plans = [];
        var storage = new StorageResult(0, 0, 0);
        var providerVersion = string.Empty;
        var totalRuns = protocol.WarmupRepetitions + protocol.Repetitions;

        for (var run = 0; run < totalRuns; run++)
        {
            var measured = run >= protocol.WarmupRepetitions;
            await database.PrepareRunAsync(run, cancellationToken);
            await using var keeper = await database.OpenAsync(cancellationToken);
            providerVersion = await database.ReadVersionAsync(keeper, cancellationToken);
            await CreateAndSeedAsync(cancellationToken);

            await RunAutomationAsync(measurements, measured, cancellationToken);
            await RunPublicChatAsync(measurements, measured, cancellationToken);
            await RunConfigurationActivationAsync(measurements, measured, cancellationToken);
            await RunPointsCommunityAsync(measurements, measured, cancellationToken);
            await RunPluginStateAsync(measurements, measured, cancellationToken);
            await RunPublicReadsAsync(measurements, measured, cancellationToken);

            var outcomes = await ReadLogicalOutcomesAsync(cancellationToken);
            ValidateOutcomes(outcomes);
            if (measured)
            {
                if (expectedOutcomes is not null && !SameOutcomes(expectedOutcomes, outcomes))
                {
                    throw new InvalidDataException(
                        "Repeated runs did not produce identical logical outcomes."
                    );
                }
                expectedOutcomes = outcomes;
                plans = await ReadQueryPlansAsync(cancellationToken);
                var measuredStorage = await database.ReadStorageAsync(cancellationToken);
                storage = new(
                    Math.Max(storage.DatabaseBytes, measuredStorage.DatabaseBytes),
                    Math.Max(storage.WalBytes, measuredStorage.WalBytes),
                    Math.Max(storage.TotalBytes, measuredStorage.TotalBytes)
                );
            }
        }

        return new(
            1,
            protocol.ProtocolId,
            protocol.SourceCommit,
            protocolDigest,
            database.Provider,
            providerVersion,
            new(
                Environment.Version.ToString(),
                RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                Environment.ProcessorCount
            ),
            protocol.Seed,
            protocol.Repetitions,
            protocol.Fixture,
            protocol
                .Workloads.Select(definition =>
                    measurements[definition.Id].ToResult(definition.Id, protocol.Repetitions)
                )
                .ToArray(),
            storage,
            plans,
            expectedOutcomes
                ?? throw new InvalidDataException("No measured workload repetition completed."),
            true
        );
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
