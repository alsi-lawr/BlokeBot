using System.Diagnostics;

namespace BlokeBot.DatabaseWorkloads;

internal sealed partial class DatabaseBaselineRunner
{
    private async Task RunPointsCommunityAsync(
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.PointsCommunityWrites];
        await RunPairedAsync(
            WorkloadId.PointsCommunityWrites,
            definition,
            measurements,
            measured,
            async (index, worker, ct) =>
                await ExecuteWithRetryAsync(
                    async (connection, transaction, token) =>
                    {
                        var logical = (index * 2) + worker;
                        var operation =
                            definition.DuplicateEvery > 0
                            && logical > 0
                            && logical % definition.DuplicateEvery == 0
                                ? logical - 1
                                : logical;
                        var inserted = await ExecuteAsync(
                            connection,
                            transaction,
                            database.InsertIgnore(
                                "INSERT OR IGNORE INTO community_source_event_receipts (\"HostId\", \"SourceKind\", \"SourceEventId\", \"ProcessedAtUtc\") VALUES (1, 'ChatMessage', $event, $now);",
                                "INSERT INTO community_source_event_receipts (\"HostId\", \"SourceKind\", \"SourceEventId\", \"ProcessedAtUtc\") VALUES (1, 'ChatMessage', $event, $now) ON CONFLICT DO NOTHING;"
                            ),
                            token,
                            ("$event", $"synthetic-community-{operation:D8}"),
                            ("$now", _epoch.AddMilliseconds(logical))
                        );
                        if (inserted == 0)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        var viewer = operation % protocol.Fixture.Viewers;
                        var login = Identity(viewer);
                        _ = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE point_balances SET \"Amount\" = CAST(CAST(\"Amount\" AS INTEGER) + 1 AS TEXT), \"UpdatedAtUtc\" = $now WHERE \"HostId\" = 1 AND \"Login\" = $login; INSERT INTO point_ledger_entries (\"HostId\", \"CreatedAtUtc\", \"Kind\", \"Login\", \"Delta\", \"BalanceAfter\", \"Note\", \"OperationKey\") SELECT 1, $now, 'Add', $login, '1', \"Amount\", '', $operation FROM point_balances WHERE \"HostId\" = 1 AND \"Login\" = $login; UPDATE community_progress SET \"Amount\" = \"Amount\" + 1, \"UpdatedAtUtc\" = $now WHERE \"HostId\" = 1 AND \"DefinitionId\" = 1 AND \"SubjectKey\" = $subject;",
                            token,
                            ("$now", _epoch.AddMilliseconds(logical)),
                            ("$login", login),
                            ("$operation", $"synthetic-points-{operation:D8}"),
                            ("$subject", $"viewer:{viewer:D8}")
                        );
                        return OperationOutcome.Committed;
                    },
                    ct
                ),
            cancellationToken
        );
    }

    private async Task RunPluginStateAsync(
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.PluginFeatureState];
        var measure = measurements[WorkloadId.PluginFeatureState];
        var elapsed = Stopwatch.StartNew();
        var rounds = (int)
            Math.Ceiling(definition.Operations / (double)protocol.Concurrency.Writers);
        for (var index = 0; index < rounds; index++)
        {
            var expected = await ReadPluginRevisionAsync(cancellationToken);
            await RunRoundAsync(async worker =>
            {
                var started = Stopwatch.GetTimestamp();
                var execution = await ExecuteWithRetryAsync(
                    async (connection, transaction, token) =>
                    {
                        var changed = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE plugin_feature_states SET \"Revision\" = \"Revision\" + 1, \"FeatureGeneration\" = \"FeatureGeneration\" + 1, \"LifecycleOperationId\" = $operation WHERE \"PluginId\" = 'synthetic-plugin' AND \"FeatureId\" = 'synthetic-feature' AND \"HostId\" = 1 AND \"Revision\" = $expected;",
                            token,
                            (
                                "$operation",
                                DeterministicGuid(protocol.Seed + 40, (index * 2) + worker)
                            ),
                            ("$expected", expected)
                        );
                        return changed == 0
                            ? OperationOutcome.ExpectedConflict
                            : OperationOutcome.Committed;
                    },
                    cancellationToken
                );
                if (measured)
                {
                    measure.Record(
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        execution.Outcome,
                        execution.BusyEvents,
                        execution.BusyWaitMilliseconds
                    );
                }
            });
        }
        if (measured)
        {
            await RecordCancellationAsync(measure);
            measure.AddElapsed(elapsed.Elapsed.TotalSeconds);
        }
    }

    private async Task RunPublicReadsAsync(
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.PublicReads];
        var measure = measurements[WorkloadId.PublicReads];
        var elapsed = Stopwatch.StartNew();
        var rounds = (int)
            Math.Ceiling(definition.Operations / (double)protocol.Concurrency.Readers);
        for (var index = 0; index < rounds; index++)
        {
            var tasks = Enumerable
                .Range(0, protocol.Concurrency.Readers)
                .Where(reader =>
                    (index * protocol.Concurrency.Readers) + reader < definition.Operations
                )
                .Select(async reader =>
                {
                    var started = Stopwatch.GetTimestamp();
                    await using var connection = await database.OpenAsync(cancellationToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT \"Login\", CAST(\"Amount\" AS INTEGER) AS \"NumericAmount\" FROM point_balances WHERE \"HostId\" = 1 ORDER BY \"NumericAmount\" DESC, \"Login\" LIMIT 50;";
                    await using var rows = await command.ExecuteReaderAsync(cancellationToken);
                    var count = 0;
                    while (await rows.ReadAsync(cancellationToken))
                    {
                        count++;
                    }
                    if (count != 50)
                    {
                        throw new InvalidDataException(
                            "The public leaderboard did not return 50 rows."
                        );
                    }
                    if (measured)
                    {
                        measure.Record(
                            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                            OperationOutcome.Committed,
                            0,
                            0
                        );
                    }
                });
            await Task.WhenAll(tasks);
        }
        if (measured)
        {
            await RecordCancellationAsync(measure);
            measure.AddElapsed(elapsed.Elapsed.TotalSeconds);
        }
    }
}
