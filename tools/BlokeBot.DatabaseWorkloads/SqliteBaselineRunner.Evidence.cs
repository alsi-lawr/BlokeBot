using System.Globalization;

namespace BlokeBot.DatabaseWorkloads;

internal sealed partial class DatabaseBaselineRunner
{
    private async Task<Dictionary<string, long>> ReadLogicalOutcomesAsync(
        CancellationToken cancellationToken
    )
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["automation_completed"] =
                "SELECT COUNT(*) FROM automation_flow_runs WHERE \"Status\" = 'Completed';",
            ["automation_receipts"] = "SELECT COUNT(*) FROM automation_event_receipts;",
            ["public_chat_expired"] =
                "SELECT COUNT(*) FROM public_chat_outbox WHERE \"Status\" = 'Expired';",
            ["configuration_complete"] =
                "SELECT COUNT(*) FROM configuration_activations WHERE \"Status\" = 'Complete';",
            ["point_ledger_rows"] = "SELECT COUNT(*) FROM point_ledger_entries;",
            ["point_balance_total"] =
                "SELECT COALESCE(SUM(CAST(\"Amount\" AS INTEGER)), 0) FROM point_balances;",
            ["community_receipts"] = "SELECT COUNT(*) FROM community_source_event_receipts;",
            ["community_progress_total"] =
                "SELECT COALESCE(SUM(\"Amount\"), 0) FROM community_progress;",
            ["plugin_revision"] =
                "SELECT \"Revision\" FROM plugin_feature_states WHERE \"PluginId\" = 'synthetic-plugin' AND \"FeatureId\" = 'synthetic-feature' AND \"HostId\" = 1;",
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

    private async Task<IReadOnlyList<QueryPlanResult>> ReadQueryPlansAsync(
        CancellationToken cancellationToken
    )
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["public_chat_outbox_claims"] =
                "SELECT \"Id\" FROM public_chat_outbox WHERE \"Status\" = 'Pending' ORDER BY \"NextAttemptAtUtc\", \"CreatedAtUtc\", \"Id\" LIMIT 1;",
            ["configuration_activation"] =
                "SELECT \"Id\", \"Revision\" FROM configuration_activations WHERE \"Status\" = 'Pending' ORDER BY \"UpdatedAtUtc\", \"Id\" LIMIT 1;",
            ["public_reads"] =
                "SELECT \"Login\", CAST(\"Amount\" AS INTEGER) AS \"NumericAmount\" FROM point_balances WHERE \"HostId\" = 1 ORDER BY \"NumericAmount\" DESC, \"Login\" LIMIT 50;",
        };
        var results = new List<QueryPlanResult>();
        foreach (var (name, sql) in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = database.Explain(sql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var steps = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                steps.Add(database.ReadPlanStep(reader));
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
}
