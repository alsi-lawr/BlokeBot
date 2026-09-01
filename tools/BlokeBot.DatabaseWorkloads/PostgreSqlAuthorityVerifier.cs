using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

internal static partial class PostgreSqlAuthorityVerifier
{
    private static readonly DateTime _now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static async Task<IReadOnlyDictionary<string, long>> VerifyAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var database = new PostgreSqlWorkloadDatabase(connectionString);
        await database.PrepareRunAsync(0, cancellationToken);
        var options = Options(database);
        var flowId = Guid.Parse("00000000-0000-0000-0000-000000000271");
        var nodeId = Guid.Parse("00000000-0000-0000-0000-000000000272");
        await SeedAsync(options, flowId, nodeId, cancellationToken);

        await using (var db = new BlokeBotDbContext(options))
        await using (
            var transaction =
                await MainDatabaseWriteTransaction.StartImmediateWithBoundedAdmissionAsync(
                    db,
                    TimeSpan.FromSeconds(1),
                    cancellationToken
                )
        )
        {
            Require(
                await MainDatabaseStatements.LockHostAsync(db, 1, cancellationToken) == 1,
                "host write admission"
            );
            await transaction.CommitAsync(cancellationToken);
        }

        await VerifyContendedAdmissionAsync(options, cancellationToken);
        await VerifyClaimBoundAsync(options, cancellationToken);
        var receiptClaims = await TwoWriterReceiptClaimAsync(options, cancellationToken);
        Require(receiptClaims.Order().SequenceEqual([0, 1]), "two-writer receipt claim");
        var serializedWrites = await TwoWriterImmediateWriteAsync(options, cancellationToken);
        Require(
            serializedWrites.Order().SequenceEqual([0, 1]),
            "two-writer immediate-write serialization"
        );

        await using (var db = new BlokeBotDbContext(options))
        {
            Require(
                await MainDatabaseStatements.TryInsertAutomationFlowRunAsync(
                    db,
                    new(
                        Guid.Parse("00000000-0000-0000-0000-000000000273"),
                        flowId,
                        1,
                        1,
                        HostFeatureFlags.Automations,
                        1,
                        "synthetic.event",
                        nodeId,
                        Guid.Parse("00000000-0000-0000-0000-000000000274"),
                        "{}",
                        "{}",
                        AutomationFlowRunStatus.Running,
                        _now
                    ),
                    cancellationToken
                ) == 1,
                "automation run admission"
            );
            Require(
                await MainDatabaseStatements.TryClaimCommunitySourceEventAsync(
                    db,
                    1,
                    CommunityEventRuleKind.ChatMessage,
                    "community-1",
                    _now,
                    cancellationToken
                ) == 1,
                "community receipt admission"
            );
            Require(
                await MainDatabaseStatements.TryClaimCustomCommandInvocationAsync(
                    db,
                    1,
                    1,
                    "viewer-1",
                    "stream-1",
                    _now,
                    cancellationToken
                ) == 1,
                "custom-command claim"
            );
            Require(
                await MainDatabaseStatements.TryClaimAutomaticRaidEventAsync(
                    db,
                    1,
                    "raid-1",
                    _now,
                    _now.AddMinutes(10),
                    cancellationToken
                ) == 1,
                "automatic-raid claim"
            );
            Require(
                await MainDatabaseStatements.EnsureViewerPassportStreamSessionAsync(
                    db,
                    1,
                    "stream-1",
                    _now,
                    1,
                    _now,
                    cancellationToken
                ) == 1,
                "viewer stream session"
            );
            var sessionId = await db
                .ViewerPassportStreamSessions.Where(value => value.HostId == 1)
                .Select(value => value.Id)
                .SingleAsync(cancellationToken);
            Require(
                await MainDatabaseStatements.TryRecordViewerPassportAttendanceAsync(
                    db,
                    1,
                    1,
                    sessionId,
                    1,
                    _now,
                    cancellationToken
                ) == 1,
                "viewer attendance"
            );
            Require(
                await MainDatabaseStatements.TryRecordViewerPassportAmbiguityAsync(
                    db,
                    1,
                    "ambiguous-viewer",
                    _now,
                    cancellationToken
                ) == 1,
                "viewer ambiguity tombstone"
            );
            Require(
                await MainDatabaseStatements.PluginAutomationFlowIdsAsync(
                    db,
                    "synthetic-plugin",
                    cancellationToken
                )
                    is [var ownedFlow]
                    && ownedFlow == flowId,
                "plugin JSON flow selection"
            );
            Require(
                await MainDatabaseStatements.TryRecordRaidCollaborationAsync(
                    db,
                    new(
                        1,
                        "collaboration-1",
                        RaidDirection.Incoming,
                        "other-1",
                        "other",
                        "Other",
                        10,
                        _now,
                        RaidWelcomeOutcome.NotConfigured,
                        RaidShoutoutOutcome.NotConfigured,
                        _now,
                        (long)HostFeatureFlags.RaidCollaboration,
                        _now
                    ),
                    cancellationToken
                ) == 1,
                "raid collaboration admission"
            );
        }

        var outcomes = await ReadOutcomesAsync(options, cancellationToken);
        Require(outcomes["serialized_revision"] == 2, "serialized revision");
        Require(outcomes["serialized_audits"] == 1, "serialized audit atomicity");
        return outcomes;
    }

    private static DbContextOptions<BlokeBotDbContext> Options(WorkloadDatabase database)
    {
        var builder = new DbContextOptionsBuilder<BlokeBotDbContext>();
        database.Configure(builder);
        return builder.Options;
    }

    private static async Task SeedAsync(
        DbContextOptions<BlokeBotDbContext> options,
        Guid flowId,
        Guid nodeId,
        CancellationToken cancellationToken
    )
    {
        await using var db = new BlokeBotDbContext(options);
        _ = await db.Database.EnsureCreatedAsync(cancellationToken);
        _ = db.Hosts.Add(
            new()
            {
                Id = 1,
                Login = "synthetic-host",
                DisplayName = "Synthetic Host",
                EnabledFeatures =
                    HostFeatureFlags.Automations
                    | HostFeatureFlags.CommunityProgression
                    | HostFeatureFlags.RaidCollaboration
                    | HostFeatureFlags.ViewerPassports,
                CreatedAtUtc = _now,
            }
        );
        _ = db.AutomationFlows.Add(
            new()
            {
                Id = flowId,
                HostId = 1,
                Name = "Synthetic plugin flow",
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
                        PluginProvenanceJson = "{\"pluginId\":\"synthetic-plugin\"}",
                    },
                ],
            }
        );
        _ = db.CustomCommands.Add(
            new()
            {
                Id = 1,
                HostId = 1,
                Name = "synthetic",
                CreatedAtUtc = _now,
                UpdatedAtUtc = _now,
                Action = new MessageCustomCommandAction { HostId = 1 },
            }
        );
        _ = db.ViewerPassports.Add(
            new()
            {
                Id = 1,
                HostId = 1,
                TwitchUserId = "viewer-1",
                Login = "viewer",
                DisplayName = "Viewer",
                CreatedAtUtc = _now,
                UpdatedAtUtc = _now,
            }
        );
        _ = db.PluginFeatureStates.Add(
            new()
            {
                PluginId = "synthetic-plugin",
                FeatureId = "serialized-feature",
                HostId = 1,
                LifecycleOperationId = Guid.Parse("00000000-0000-0000-0000-000000000275"),
                WorkerGeneration = 1,
                FeatureGeneration = 1,
                Readiness = PluginFeatureReadinessKind.Ready,
                Revision = 1,
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
    }
}
