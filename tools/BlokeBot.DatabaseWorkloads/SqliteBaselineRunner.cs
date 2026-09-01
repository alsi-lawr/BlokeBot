using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace BlokeBot.DatabaseWorkloads;

public sealed partial class SqliteBaselineRunner(WorkloadProtocol protocol, string protocolDigest)
{
    private static readonly DateTime _epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<WorkloadId, WorkloadDefinition> _definitions =
        protocol.Workloads.ToDictionary(static definition => definition.Id);

    public async Task<BaselineResult> RunAsync(
        string databasePath,
        CancellationToken cancellationToken
    )
    {
        var fullPath = Path.GetFullPath(databasePath);
        RefuseExisting(fullPath);
        ThreadPool.GetMinThreads(out var minimumWorkerThreads, out var minimumIoThreads);
        _ = ThreadPool.SetMinThreads(
            Math.Max(minimumWorkerThreads, protocol.Concurrency.Writers * 4),
            minimumIoThreads
        );
        var parent =
            Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The database path must have a parent directory.");
        _ = Directory.CreateDirectory(parent);

        var measurements = protocol.Workloads.ToDictionary(
            static definition => definition.Id,
            static _ => new WorkloadMeasurements()
        );
        Dictionary<string, long>? expectedOutcomes = null;
        IReadOnlyList<QueryPlanResult> plans = [];
        long databaseBytes = 0;
        long walBytes = 0;
        string sqliteVersion = string.Empty;
        var totalRuns = protocol.WarmupRepetitions + protocol.Repetitions;

        for (var run = 0; run < totalRuns; run++)
        {
            var measured = run >= protocol.WarmupRepetitions;
            if (run > 0)
            {
                DeleteOwnedDatabase(fullPath);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 0,
            }.ToString();
            await using var keeper = await OpenAsync(connectionString, cancellationToken);
            await ConfigureDatabaseAsync(keeper, cancellationToken);
            sqliteVersion = await ScalarStringAsync(
                keeper,
                "SELECT sqlite_version();",
                cancellationToken
            );
            await CreateAndSeedAsync(connectionString, cancellationToken);

            await RunAutomationAsync(connectionString, measurements, measured, cancellationToken);
            await RunPublicChatAsync(connectionString, measurements, measured, cancellationToken);
            await RunConfigurationActivationAsync(
                connectionString,
                measurements,
                measured,
                cancellationToken
            );
            await RunPointsCommunityAsync(
                connectionString,
                measurements,
                measured,
                cancellationToken
            );
            await RunPluginStateAsync(connectionString, measurements, measured, cancellationToken);
            await RunPublicReadsAsync(connectionString, measurements, measured, cancellationToken);

            var outcomes = await ReadLogicalOutcomesAsync(connectionString, cancellationToken);
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
                plans = await ReadQueryPlansAsync(connectionString, cancellationToken);
                await CheckpointAsync(keeper, cancellationToken);
                databaseBytes = Math.Max(databaseBytes, FileLength(fullPath));
                walBytes = Math.Max(walBytes, FileLength(fullPath + "-wal"));
            }
        }

        return new(
            1,
            protocol.ProtocolId,
            protocol.SourceCommit,
            protocolDigest,
            "sqlite",
            sqliteVersion,
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
            new(databaseBytes, walBytes, databaseBytes + walBytes),
            plans,
            expectedOutcomes
                ?? throw new InvalidDataException("No measured workload repetition completed."),
            true
        );
    }

    public static void RefuseExisting(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (
            File.Exists(fullPath)
            || File.Exists(fullPath + "-wal")
            || File.Exists(fullPath + "-shm")
        )
        {
            throw new IOException(
                "The SQLite baseline requires a new database path and never overwrites an existing database."
            );
        }
    }

    private static long FileLength(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void DeleteOwnedDatabase(string path)
    {
        foreach (var ownedPath in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(ownedPath))
            {
                File.Delete(ownedPath);
            }
        }
    }
}
