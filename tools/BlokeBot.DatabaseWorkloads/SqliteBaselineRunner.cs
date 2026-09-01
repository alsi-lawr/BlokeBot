using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

public sealed class SqliteBaselineRunner(WorkloadProtocol protocol, string protocolDigest)
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

    private async Task CreateAndSeedAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var db = new BlokeBotDbContext(options);
        _ = await db.Database.EnsureCreatedAsync(cancellationToken);
        var flowId = DeterministicGuid(protocol.Seed, 1);
        var nodeId = DeterministicGuid(protocol.Seed, 2);
        var season = new CommunitySeason
        {
            Id = 1,
            PublicId = DeterministicGuid(protocol.Seed, 3),
            HostId = 1,
            CreationOperationId = DeterministicGuid(protocol.Seed, 4),
            Name = "Synthetic season",
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Public,
            StartsAtUtc = _epoch,
            EndsAtUtc = _epoch.AddYears(1),
            OpenedAtUtc = _epoch,
            Revision = 1,
            CreatedAtUtc = _epoch,
            UpdatedAtUtc = _epoch,
        };
        season.Definitions.Add(
            new CommunityDefinition
            {
                Id = 1,
                PublicId = DeterministicGuid(protocol.Seed, 5),
                HostId = 1,
                Key = "synthetic-chat",
                Name = "Synthetic chat activity",
                Kind = CommunityDefinitionKind.Quest,
                Scope = CommunityProgressScope.Viewer,
                CompletionMode = CommunityCompletionMode.Repeatable,
                EventRule = CommunityEventRuleKind.ChatMessage,
                Increment = CommunityProgressIncrement.Occurrence,
                Target = 100,
                ResetCadence = CommunityResetCadence.None,
                ScheduleRevision = 1,
                CreatedAtUtc = _epoch,
            }
        );
        _ = db.Hosts.Add(
            new BotHost
            {
                Id = 1,
                Login = "synthetic-host",
                DisplayName = "Synthetic Host",
                EnabledFeatures =
                    HostFeatureFlags.Automations
                    | HostFeatureFlags.Points
                    | HostFeatureFlags.CommunityProgression,
                CreatedAtUtc = _epoch,
            }
        );
        _ = db.AutomationFlows.Add(
            new AutomationFlow
            {
                Id = flowId,
                HostId = 1,
                Name = "Synthetic flow",
                SchemaVersion = 1,
                IsEnabled = true,
                CreatedAtUtc = _epoch,
                UpdatedAtUtc = _epoch,
                Nodes =
                [
                    new AutomationFlowNode
                    {
                        Id = nodeId,
                        DefinitionId = "synthetic.action",
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = "{}",
                        InputBindingsJson = "{}",
                        ExpressionLanguageVersion = 1,
                    },
                ],
            }
        );
        _ = db.CommunitySeasons.Add(season);
        _ = db.PluginFeatureStates.Add(
            new PluginFeatureStateRecord
            {
                PluginId = "synthetic-plugin",
                FeatureId = "synthetic-feature",
                HostId = 1,
                LifecycleOperationId = DeterministicGuid(protocol.Seed, 6),
                WorkerGeneration = 1,
                FeatureGeneration = 1,
                Readiness = PluginFeatureReadinessKind.Ready,
                Revision = 1,
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);

        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        for (var index = 0; index < protocol.Fixture.Viewers; index++)
        {
            var login = Identity(index);
            _ = await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO point_balances (HostId, Login, Amount, UpdatedAtUtc) VALUES (1, $login, $amount, $now);",
                cancellationToken,
                ("$login", login),
                ("$amount", (index % 1000).ToString(CultureInfo.InvariantCulture)),
                ("$now", _epoch)
            );
            _ = await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO community_progress (HostId, SeasonId, DefinitionId, SubjectKey, ViewerTwitchUserId, ViewerLogin, ViewerDisplayName, Amount, CompletionCount, UpdatedAtUtc) VALUES (1, 1, 1, $subject, $viewerId, $login, $display, $amount, 0, $now);",
                cancellationToken,
                ("$subject", $"viewer:{index:D8}"),
                ("$viewerId", $"synthetic-{index:D8}"),
                ("$login", login),
                ("$display", $"Synthetic {index:D8}"),
                ("$amount", index % 100),
                ("$now", _epoch)
            );
        }
        for (var index = 0; index < protocol.Fixture.PublicChatBacklog; index++)
        {
            _ = await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO public_chat_outbox (Channel, Message, DeduplicationKey, CreatedAtUtc, ExpiresAtUtc, NextAttemptAtUtc, Status, AttemptCount, SafePreSendFailureCount) VALUES ('synthetic-host', $message, $key, $created, $expires, $created, 'Pending', 0, 0);",
                cancellationToken,
                ("$message", $"synthetic-message-{index:D8}"),
                ("$key", new string('0', 56) + index.ToString("x8", CultureInfo.InvariantCulture)),
                ("$created", _epoch.AddMilliseconds(index)),
                ("$expires", _epoch.AddDays(1))
            );
        }
        transaction.Commit();
    }

    private async Task RunAutomationAsync(
        string connectionString,
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.AutomationAdmissionCheckpointing];
        await RunPairedAsync(
            WorkloadId.AutomationAdmissionCheckpointing,
            definition,
            measurements,
            measured,
            async (index, worker, ct) =>
                await ExecuteWithRetryAsync(
                    connectionString,
                    async (connection, transaction, token) =>
                    {
                        var logical = (index * protocol.Concurrency.Writers) + worker;
                        var occurrence =
                            definition.DuplicateEvery > 0
                            && logical > 0
                            && logical % definition.DuplicateEvery == 0
                                ? logical - 1
                                : logical;
                        var inserted = await ExecuteAsync(
                            connection,
                            transaction,
                            "INSERT OR IGNORE INTO automation_event_receipts (HostId, SourceDefinitionId, ProviderMessageId, ClaimedAtUtc, ExpiresAtUtc) VALUES (1, 'synthetic.event', $message, $now, $expires);",
                            token,
                            ("$message", $"synthetic-event-{occurrence:D8}"),
                            ("$now", _epoch.AddMilliseconds(logical)),
                            ("$expires", _epoch.AddMinutes(10))
                        );
                        if (inserted == 0)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        var runId = DeterministicGuid(protocol.Seed + 10, occurrence);
                        _ = await ExecuteAsync(
                            connection,
                            transaction,
                            "INSERT INTO automation_flow_runs (Id, FlowId, HostId, AutomationGeneration, RequiredFeatures, ContextSchemaVersion, SourceDefinitionId, SourceNodeId, SourceOccurrenceId, ContextJson, DefinitionJson, Status, StartedAtUtc) VALUES ($run, $flow, 1, 1, 4096, 1, 'synthetic.event', $node, $occurrence, '{}', '{}', 'Running', $now);",
                            token,
                            ("$run", runId),
                            ("$flow", DeterministicGuid(protocol.Seed, 1)),
                            ("$node", DeterministicGuid(protocol.Seed, 2)),
                            ("$occurrence", DeterministicGuid(protocol.Seed + 11, occurrence)),
                            ("$now", _epoch.AddMilliseconds(logical))
                        );
                        _ = await ExecuteAsync(
                            connection,
                            transaction,
                            "INSERT INTO automation_node_runs (RunId, NodeId, Sequence, Status, AvailableAtUtc) VALUES ($run, $node, 1, 'Pending', $now); UPDATE automation_node_runs SET Status = 'Succeeded', StartedAtUtc = $now, CompletedAtUtc = $now, OutcomeCode = 'ok', OutputJson = '{}' WHERE RunId = $run; UPDATE automation_flow_runs SET Status = 'Completed', CompletedAtUtc = $now WHERE Id = $run;",
                            token,
                            ("$run", runId),
                            ("$node", DeterministicGuid(protocol.Seed, 2)),
                            ("$now", _epoch.AddMilliseconds(logical + 1))
                        );
                        return OperationOutcome.Committed;
                    },
                    ct
                ),
            cancellationToken
        );
    }

    private async Task RunPublicChatAsync(
        string connectionString,
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.PublicChatOutboxClaims];
        await RunPairedAsync(
            WorkloadId.PublicChatOutboxClaims,
            definition,
            measurements,
            measured,
            async (index, worker, ct) =>
                await ExecuteWithRetryAsync(
                    connectionString,
                    async (connection, transaction, token) =>
                    {
                        var id = await ScalarLongAsync(
                            connection,
                            transaction,
                            "SELECT Id FROM public_chat_outbox WHERE Status = 'Pending' ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id LIMIT 1;",
                            token
                        );
                        if (id is null)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        var changed = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE public_chat_outbox SET Status = 'Claimed', ClaimToken = $claim, ClaimSlot = 1, ClaimExpiresAtUtc = $expires WHERE Id = $id AND Status = 'Pending' AND ClaimSlot IS NULL;",
                            token,
                            ("$claim", DeterministicGuid(protocol.Seed + 20, (index * 2) + worker)),
                            ("$expires", _epoch.AddMinutes(1)),
                            ("$id", id.Value)
                        );
                        if (changed == 0)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        _ = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE public_chat_outbox SET Status = 'Expired', Message = NULL, DeduplicationKey = NULL, NextAttemptAtUtc = NULL, ClaimToken = NULL, ClaimSlot = NULL, ClaimExpiresAtUtc = NULL, CompletedAtUtc = $now WHERE Id = $id;",
                            token,
                            ("$now", _epoch.AddMinutes(2)),
                            ("$id", id.Value)
                        );
                        return OperationOutcome.Committed;
                    },
                    ct
                ),
            cancellationToken
        );
    }

    private async Task RunConfigurationActivationAsync(
        string connectionString,
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.ConfigurationActivation];
        await using (var connection = await OpenAsync(connectionString, cancellationToken))
        await using (var transaction = connection.BeginTransaction(deferred: false))
        {
            for (var index = 0; index < definition.Operations; index++)
            {
                _ = await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO configuration_activations (Id, HostId, EnabledChanges, DisabledChanges, Status, AttemptCount, Revision, CreatedAtUtc, UpdatedAtUtc) VALUES ($id, 1, 2, 0, 'Pending', 0, 1, $now, $now);",
                    cancellationToken,
                    ("$id", DeterministicGuid(protocol.Seed + 30, index)),
                    ("$now", _epoch.AddMilliseconds(index))
                );
            }
            transaction.Commit();
        }
        await RunPairedAsync(
            WorkloadId.ConfigurationActivation,
            definition,
            measurements,
            measured,
            async (index, worker, ct) =>
                await ExecuteWithRetryAsync(
                    connectionString,
                    async (connection, transaction, token) =>
                    {
                        var row = await ReadPairAsync(
                            connection,
                            transaction,
                            "SELECT Id, Revision FROM configuration_activations WHERE Status = 'Pending' ORDER BY UpdatedAtUtc, Id LIMIT 1;",
                            token
                        );
                        if (row is null)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        var changed = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE configuration_activations SET Status = 'Processing', AttemptCount = AttemptCount + 1, Revision = Revision + 1, UpdatedAtUtc = $now WHERE Id = $id AND Revision = $revision AND Status = 'Pending'; UPDATE configuration_activations SET Status = 'Complete', CompletedAtUtc = $now WHERE Id = $id AND Status = 'Processing';",
                            token,
                            ("$now", _epoch.AddMinutes(3)),
                            ("$id", row.Value.Id),
                            ("$revision", row.Value.Revision)
                        );
                        return changed == 0
                            ? OperationOutcome.ExpectedConflict
                            : OperationOutcome.Committed;
                    },
                    ct
                ),
            cancellationToken
        );
    }

    private async Task RunPointsCommunityAsync(
        string connectionString,
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
                    connectionString,
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
                            "INSERT OR IGNORE INTO community_source_event_receipts (HostId, SourceKind, SourceEventId, ProcessedAtUtc) VALUES (1, 'ChatMessage', $event, $now);",
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
                            "UPDATE point_balances SET Amount = CAST(CAST(Amount AS INTEGER) + 1 AS TEXT), UpdatedAtUtc = $now WHERE HostId = 1 AND Login = $login; INSERT INTO point_ledger_entries (HostId, CreatedAtUtc, Kind, Login, Delta, BalanceAfter, Note, OperationKey) SELECT 1, $now, 'Add', $login, '1', Amount, '', $operation FROM point_balances WHERE HostId = 1 AND Login = $login; UPDATE community_progress SET Amount = Amount + 1, UpdatedAtUtc = $now WHERE HostId = 1 AND DefinitionId = 1 AND SubjectKey = $subject;",
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
        string connectionString,
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
            var expected = await ReadPluginRevisionAsync(connectionString, cancellationToken);
            await RunRoundAsync(async worker =>
            {
                var started = Stopwatch.GetTimestamp();
                var execution = await ExecuteWithRetryAsync(
                    connectionString,
                    async (connection, transaction, token) =>
                    {
                        var changed = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE plugin_feature_states SET Revision = Revision + 1, FeatureGeneration = FeatureGeneration + 1, LifecycleOperationId = $operation WHERE PluginId = 'synthetic-plugin' AND FeatureId = 'synthetic-feature' AND HostId = 1 AND Revision = $expected;",
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
        string connectionString,
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
                    await using var connection = await OpenAsync(
                        connectionString,
                        cancellationToken
                    );
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT Login, CAST(Amount AS INTEGER) AS NumericAmount FROM point_balances WHERE HostId = 1 ORDER BY NumericAmount DESC, Login LIMIT 50;";
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

    private async Task RunPairedAsync(
        WorkloadId id,
        WorkloadDefinition definition,
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        Func<int, int, CancellationToken, Task<ExecutionResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var measure = measurements[id];
        var elapsed = Stopwatch.StartNew();
        var rounds = (int)
            Math.Ceiling(definition.Operations / (double)protocol.Concurrency.Writers);
        for (var index = 0; index < rounds; index++)
        {
            await RunRoundAsync(async worker =>
            {
                var logical = (index * protocol.Concurrency.Writers) + worker;
                if (logical >= definition.Operations)
                {
                    return;
                }
                var started = Stopwatch.GetTimestamp();
                var execution = await operation(index, worker, cancellationToken);
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

    private static async Task RunRoundAsync(Func<int, Task> operation)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            await gate.Task;
            await operation(0);
        });
        var second = Task.Run(async () =>
        {
            await gate.Task;
            await operation(1);
        });
        gate.SetResult();
        await Task.WhenAll(first, second);
    }

    private async Task<ExecutionResult> ExecuteWithRetryAsync(
        string connectionString,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<OperationOutcome>> action,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        long busyEvents = 0;
        var busyWait = TimeSpan.Zero;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var connection = await OpenAsync(connectionString, cancellationToken);
                var admissionStarted = Stopwatch.GetTimestamp();
                await using var transaction = connection.BeginTransaction(deferred: false);
                var admissionWait = Stopwatch.GetElapsedTime(admissionStarted);
                if (admissionWait >= TimeSpan.FromMilliseconds(1))
                {
                    busyEvents++;
                    busyWait += admissionWait;
                }
                var outcome = await action(connection, transaction, cancellationToken);
                transaction.Commit();
                return new(outcome, busyEvents, busyWait.TotalMilliseconds);
            }
            catch (SqliteException exception)
                when (exception.SqliteErrorCode is 5 or 6
                    && attempt < protocol.Concurrency.MaxBusyRetries
                )
            {
                busyEvents++;
                var waitStarted = Stopwatch.GetTimestamp();
                await Task.Delay(
                    protocol.Concurrency.BusyRetryDelayMilliseconds,
                    cancellationToken
                );
                busyWait += Stopwatch.GetElapsedTime(waitStarted);
            }
        }
    }

    private static async Task<SqliteConnection> OpenAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 0;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task ConfigureDatabaseAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            _ = command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> ScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<(Guid Id, long Revision)?> ReadPairAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetInt64(1))
            : null;
    }

    private static async Task<string> ScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            ) ?? string.Empty;
    }

    private static async Task CheckpointAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadPluginRevisionAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Revision FROM plugin_feature_states WHERE PluginId = 'synthetic-plugin' AND FeatureId = 'synthetic-feature' AND HostId = 1;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private async Task RecordCancellationAsync(WorkloadMeasurements measurements)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            _ = await ExecuteWithRetryAsync(
                string.Empty,
                static (_, _, _) => Task.FromResult(OperationOutcome.Committed),
                cancellation.Token
            );
            throw new InvalidDataException(
                "The frozen pre-admission cancellation was not observed."
            );
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            measurements.Record(0, OperationOutcome.Cancelled, 0, 0);
        }
    }

    private static async Task<Dictionary<string, long>> ReadLogicalOutcomesAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["automation_completed"] =
                "SELECT COUNT(*) FROM automation_flow_runs WHERE Status = 'Completed';",
            ["automation_receipts"] = "SELECT COUNT(*) FROM automation_event_receipts;",
            ["public_chat_expired"] =
                "SELECT COUNT(*) FROM public_chat_outbox WHERE Status = 'Expired';",
            ["configuration_complete"] =
                "SELECT COUNT(*) FROM configuration_activations WHERE Status = 'Complete';",
            ["point_ledger_rows"] = "SELECT COUNT(*) FROM point_ledger_entries;",
            ["point_balance_total"] =
                "SELECT COALESCE(SUM(CAST(Amount AS INTEGER)), 0) FROM point_balances;",
            ["community_receipts"] = "SELECT COUNT(*) FROM community_source_event_receipts;",
            ["community_progress_total"] =
                "SELECT COALESCE(SUM(Amount), 0) FROM community_progress;",
            ["plugin_revision"] =
                "SELECT Revision FROM plugin_feature_states WHERE PluginId = 'synthetic-plugin' AND FeatureId = 'synthetic-feature' AND HostId = 1;",
        };
        var outcomes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (name, sql) in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            outcomes[name] = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            );
        }
        return outcomes;
    }

    private static async Task<IReadOnlyList<QueryPlanResult>> ReadQueryPlansAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["public_chat_outbox_claims"] =
                "SELECT Id FROM public_chat_outbox WHERE Status = 'Pending' ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id LIMIT 1;",
            ["configuration_activation"] =
                "SELECT Id, Revision FROM configuration_activations WHERE Status = 'Pending' ORDER BY UpdatedAtUtc, Id LIMIT 1;",
            ["public_reads"] =
                "SELECT Login, CAST(Amount AS INTEGER) AS NumericAmount FROM point_balances WHERE HostId = 1 ORDER BY NumericAmount DESC, Login LIMIT 50;",
        };
        var results = new List<QueryPlanResult>();
        foreach (var (name, sql) in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var steps = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                steps.Add(reader.GetString(3));
            }
            results.Add(new(name, steps));
        }
        return results;
    }

    private static bool SameOutcomes(
        IReadOnlyDictionary<string, long> first,
        IReadOnlyDictionary<string, long> second
    ) =>
        first.Count == second.Count
        && first.All(pair => second.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private void ValidateOutcomes(IReadOnlyDictionary<string, long> outcomes)
    {
        var automation = UniqueOperations(
            _definitions[WorkloadId.AutomationAdmissionCheckpointing]
        );
        var pointsCommunity = UniqueOperations(_definitions[WorkloadId.PointsCommunityWrites]);
        var initialPoints = Enumerable
            .Range(0, protocol.Fixture.Viewers)
            .Sum(static index => index % 1000);
        var initialCommunity = Enumerable
            .Range(0, protocol.Fixture.Viewers)
            .Sum(static index => index % 100);
        var expected = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["automation_completed"] = automation,
            ["automation_receipts"] = automation,
            ["public_chat_expired"] = Math.Min(
                protocol.Fixture.PublicChatBacklog,
                _definitions[WorkloadId.PublicChatOutboxClaims].Operations
            ),
            ["configuration_complete"] = _definitions[
                WorkloadId.ConfigurationActivation
            ].Operations,
            ["point_ledger_rows"] = pointsCommunity,
            ["point_balance_total"] = initialPoints + pointsCommunity,
            ["community_receipts"] = pointsCommunity,
            ["community_progress_total"] = initialCommunity + pointsCommunity,
            ["plugin_revision"] =
                1
                + (int)
                    Math.Ceiling(
                        _definitions[WorkloadId.PluginFeatureState].Operations
                            / (double)protocol.Concurrency.Writers
                    ),
        };
        if (!SameOutcomes(expected, outcomes))
        {
            throw new InvalidDataException(
                "The workload run violated one or more frozen logical outcomes."
            );
        }
    }

    private static int UniqueOperations(WorkloadDefinition definition) =>
        definition.DuplicateEvery <= 0
            ? definition.Operations
            : definition.Operations - ((definition.Operations - 1) / definition.DuplicateEvery);

    private static Guid DeterministicGuid(int seed, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = BitConverter.TryWriteBytes(bytes, seed);
        _ = BitConverter.TryWriteBytes(bytes[4..], index);
        _ = BitConverter.TryWriteBytes(bytes[8..], ((long)seed << 32) | (uint)index);
        return new(bytes);
    }

    private static string Identity(int index) => $"viewer-{index:D8}";

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

    private sealed record ExecutionResult(
        OperationOutcome Outcome,
        long BusyEvents,
        double BusyWaitMilliseconds
    );
}
