using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

internal static partial class PostgreSqlAuthorityVerifier
{
    private static readonly Guid _malformedProvenanceFlowId = Guid.Parse(
        "00000000-0000-0000-0000-000000000277"
    );
    private static readonly Guid _ledgerOwnedFlowId = Guid.Parse(
        "00000000-0000-0000-0000-000000000279"
    );

    private static void SeedPluginJsonFixtures(BlokeBotDbContext db)
    {
        _ = db.AutomationFlows.Add(
            Flow(
                _malformedProvenanceFlowId,
                Guid.Parse("00000000-0000-0000-0000-000000000278"),
                "Malformed provenance",
                "{not-json"
            )
        );
        _ = db.AutomationFlows.Add(
            Flow(
                _ledgerOwnedFlowId,
                Guid.Parse("00000000-0000-0000-0000-00000000027a"),
                "Ledger-owned flow",
                null
            )
        );
        _ = db.PluginAutomationInstantiations.Add(
            new()
            {
                Id = Guid.Parse("00000000-0000-0000-0000-00000000027b"),
                EnableOperationId = Guid.Parse("00000000-0000-0000-0000-00000000027c"),
                PluginId = "synthetic-plugin",
                FeatureId = "synthetic-feature",
                HostId = 1,
                TemplateId = "ledger-template",
                PluginVersion = "1.0.0",
                MutableTag = "stable",
                ManifestVersion = 1,
                TemplateHash = new string('a', 64),
                Status = PluginAutomationInstantiationStatus.Completed,
                FlowId = _ledgerOwnedFlowId,
                CreatedAtUtc = _now,
                UpdatedAtUtc = _now,
            }
        );
    }

    private static async Task<IReadOnlyDictionary<string, long>> VerifyPluginJsonRemovalAsync(
        DbContextOptions<BlokeBotDbContext> options,
        Guid validProvenanceFlowId,
        CancellationToken cancellationToken
    )
    {
        var selectedCount = 0L;
        await using (var db = new BlokeBotDbContext(options))
        {
            var selected = await MainDatabaseStatements.PluginAutomationFlowIdsAsync(
                db,
                "synthetic-plugin",
                cancellationToken
            );
            Require(
                selected
                    .Order()
                    .SequenceEqual(new[] { validProvenanceFlowId, _ledgerOwnedFlowId }.Order()),
                "valid provenance and ledger flow selection with malformed provenance present"
            );
            selectedCount = selected.LongLength;
        }

        Require(PluginId.TryCreate("synthetic-plugin", out var pluginId), "synthetic plugin ID");
        var store = new EfPluginFeatureStore(new VerificationDbContextFactory(options), new());
        await store.RemovePluginDataAsync(pluginId, cancellationToken);

        await using var verify = new BlokeBotDbContext(options);
        var remainingFlowIds = await verify
            .AutomationFlows.Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var malformedRemaining = remainingFlowIds.LongCount(value =>
            value == _malformedProvenanceFlowId
        );
        var ownedRemaining = remainingFlowIds.LongCount(value =>
            value == validProvenanceFlowId || value == _ledgerOwnedFlowId
        );
        var ledgersRemaining = await verify.PluginAutomationInstantiations.LongCountAsync(
            cancellationToken
        );
        Require(
            remainingFlowIds.SequenceEqual([_malformedProvenanceFlowId]),
            "plugin removal preserves malformed unowned provenance"
        );
        Require(ledgersRemaining == 0, "plugin removal clears ledger ownership");
        return new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["plugin_owned_flows_selected"] = selectedCount,
            ["plugin_owned_flows_remaining"] = ownedRemaining,
            ["plugin_malformed_flows_remaining"] = malformedRemaining,
            ["plugin_ownership_ledgers_remaining"] = ledgersRemaining,
        };
    }

    private static AutomationFlow Flow(
        Guid flowId,
        Guid nodeId,
        string name,
        string? provenanceJson
    ) =>
        new()
        {
            Id = flowId,
            HostId = 1,
            Name = name,
            SchemaVersion = 1,
            IsEnabled = true,
            CreatedAtUtc = _now,
            UpdatedAtUtc = _now,
            Nodes =
            [
                new()
                {
                    Id = nodeId,
                    DefinitionId = "plugin.synthetic.action",
                    DefinitionSchemaVersion = 1,
                    ConfigurationJson = "{}",
                    InputBindingsJson = "{}",
                    ExpressionLanguageVersion = 1,
                    PluginProvenanceJson = provenanceJson,
                },
            ],
        };

    private sealed class VerificationDbContextFactory(DbContextOptions<BlokeBotDbContext> options)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() => new(options);

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
