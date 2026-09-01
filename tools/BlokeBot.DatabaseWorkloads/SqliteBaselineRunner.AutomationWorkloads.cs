namespace BlokeBot.DatabaseWorkloads;

internal sealed partial class DatabaseBaselineRunner
{
    private async Task RunAutomationAsync(
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
                            database.InsertIgnore(
                                "INSERT OR IGNORE INTO automation_event_receipts (\"HostId\", \"SourceDefinitionId\", \"ProviderMessageId\", \"ClaimedAtUtc\", \"ExpiresAtUtc\") VALUES (1, 'synthetic.event', $message, $now, $expires);",
                                "INSERT INTO automation_event_receipts (\"HostId\", \"SourceDefinitionId\", \"ProviderMessageId\", \"ClaimedAtUtc\", \"ExpiresAtUtc\") VALUES (1, 'synthetic.event', $message, $now, $expires) ON CONFLICT DO NOTHING;"
                            ),
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
                            "INSERT INTO automation_flow_runs (\"Id\", \"FlowId\", \"HostId\", \"AutomationGeneration\", \"RequiredFeatures\", \"ContextSchemaVersion\", \"SourceDefinitionId\", \"SourceNodeId\", \"SourceOccurrenceId\", \"ContextJson\", \"DefinitionJson\", \"Status\", \"StartedAtUtc\") VALUES ($run, $flow, 1, 1, 4096, 1, 'synthetic.event', $node, $occurrence, '{}', '{}', 'Running', $now);",
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
                            "INSERT INTO automation_node_runs (\"RunId\", \"NodeId\", \"Sequence\", \"Status\", \"AvailableAtUtc\") VALUES ($run, $node, 1, 'Pending', $now); UPDATE automation_node_runs SET \"Status\" = 'Succeeded', \"StartedAtUtc\" = $now, \"CompletedAtUtc\" = $now, \"OutcomeCode\" = 'ok', \"OutputJson\" = '{}' WHERE \"RunId\" = $run; UPDATE automation_flow_runs SET \"Status\" = 'Completed', \"CompletedAtUtc\" = $now WHERE \"Id\" = $run;",
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
                    async (connection, transaction, token) =>
                    {
                        var id = await ScalarLongAsync(
                            connection,
                            transaction,
                            "SELECT \"Id\" FROM public_chat_outbox WHERE \"Status\" = 'Pending' ORDER BY \"NextAttemptAtUtc\", \"CreatedAtUtc\", \"Id\" LIMIT 1;",
                            token
                        );
                        if (id is null)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        var changed = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE public_chat_outbox SET \"Status\" = 'Claimed', \"ClaimToken\" = $claim, \"ClaimSlot\" = 1, \"ClaimExpiresAtUtc\" = $expires WHERE \"Id\" = $id AND \"Status\" = 'Pending' AND \"ClaimSlot\" IS NULL;",
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
                            "UPDATE public_chat_outbox SET \"Status\" = 'Expired', \"Message\" = NULL, \"DeduplicationKey\" = NULL, \"NextAttemptAtUtc\" = NULL, \"ClaimToken\" = NULL, \"ClaimSlot\" = NULL, \"ClaimExpiresAtUtc\" = NULL, \"CompletedAtUtc\" = $now WHERE \"Id\" = $id;",
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
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        CancellationToken cancellationToken
    )
    {
        var definition = _definitions[WorkloadId.ConfigurationActivation];
        await using (var connection = await database.OpenAsync(cancellationToken))
        await using (
            var transaction = await database.BeginWriteAsync(connection, cancellationToken)
        )
        {
            for (var index = 0; index < definition.Operations; index++)
            {
                _ = await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT INTO configuration_activations (\"Id\", \"HostId\", \"EnabledChanges\", \"DisabledChanges\", \"Status\", \"AttemptCount\", \"Revision\", \"CreatedAtUtc\", \"UpdatedAtUtc\") VALUES ($id, 1, 2, 0, 'Pending', 0, 1, $now, $now);",
                    cancellationToken,
                    ("$id", DeterministicGuid(protocol.Seed + 30, index).ToString("D")),
                    ("$now", _epoch.AddMilliseconds(index))
                );
            }
            await transaction.CommitAsync(cancellationToken);
        }
        await RunPairedAsync(
            WorkloadId.ConfigurationActivation,
            definition,
            measurements,
            measured,
            async (index, worker, ct) =>
                await ExecuteWithRetryAsync(
                    async (connection, transaction, token) =>
                    {
                        var row = await ReadPairAsync(
                            connection,
                            transaction,
                            "SELECT \"Id\", \"Revision\" FROM configuration_activations WHERE \"Status\" = 'Pending' ORDER BY \"UpdatedAtUtc\", \"Id\" LIMIT 1;",
                            token
                        );
                        if (row is null)
                        {
                            return OperationOutcome.ExpectedConflict;
                        }
                        var changed = await ExecuteAsync(
                            connection,
                            transaction,
                            "UPDATE configuration_activations SET \"Status\" = 'Processing', \"AttemptCount\" = \"AttemptCount\" + 1, \"Revision\" = \"Revision\" + 1, \"UpdatedAtUtc\" = $now WHERE \"Id\" = $id AND \"Revision\" = $revision AND \"Status\" = 'Pending'; UPDATE configuration_activations SET \"Status\" = 'Complete', \"CompletedAtUtc\" = $now WHERE \"Id\" = $id AND \"Status\" = 'Processing';",
                            token,
                            ("$now", _epoch.AddMinutes(3)),
                            ("$id", row.Value.Id.ToString("D")),
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
}
