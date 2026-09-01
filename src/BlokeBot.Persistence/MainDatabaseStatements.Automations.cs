using BlokeBot.Persistence.Models;

namespace BlokeBot.Persistence;

public sealed record AutomationFlowRunInsert(
    Guid Id,
    Guid FlowId,
    int HostId,
    int AutomationGeneration,
    HostFeatureFlags RequiredFeatures,
    int ContextSchemaVersion,
    string SourceDefinitionId,
    Guid SourceNodeId,
    Guid SourceOccurrenceId,
    string ContextJson,
    string DefinitionJson,
    AutomationFlowRunStatus Status,
    DateTime StartedAtUtc
);

public static partial class MainDatabaseStatements
{
    public static Task<int> TryInsertAutomationFlowRunAsync(
        BlokeBotDbContext db,
        AutomationFlowRunInsert run,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO automation_flow_runs
                ("Id", "FlowId", "HostId", "AutomationGeneration", "RequiredFeatures",
                 "ContextSchemaVersion", "SourceDefinitionId", "SourceNodeId", "SourceOccurrenceId",
                 "ContextJson", "DefinitionJson", "Status", "StartedAtUtc", "CompletedAtUtc",
                 "ExecutionLeaseId")
            VALUES
                ({run.Id}, {run.FlowId}, {run.HostId}, {run.AutomationGeneration},
                 {(long)run.RequiredFeatures}, {run.ContextSchemaVersion}, {run.SourceDefinitionId},
                 {run.SourceNodeId}, {run.SourceOccurrenceId}, {run.ContextJson},
                 {run.DefinitionJson}, {run.Status.ToString()}, {run.StartedAtUtc}, NULL, NULL);
            """,
            $"""
            INSERT INTO automation_flow_runs
                ("Id", "FlowId", "HostId", "AutomationGeneration", "RequiredFeatures",
                 "ContextSchemaVersion", "SourceDefinitionId", "SourceNodeId", "SourceOccurrenceId",
                 "ContextJson", "DefinitionJson", "Status", "StartedAtUtc", "CompletedAtUtc",
                 "ExecutionLeaseId")
            VALUES
                ({run.Id}, {run.FlowId}, {run.HostId}, {run.AutomationGeneration},
                 {(long)run.RequiredFeatures}, {run.ContextSchemaVersion}, {run.SourceDefinitionId},
                 {run.SourceNodeId}, {run.SourceOccurrenceId}, {run.ContextJson},
                 {run.DefinitionJson}, {run.Status.ToString()}, {run.StartedAtUtc}, NULL, NULL)
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );

    public static Task<int> DeleteExpiredAutomationEventReceiptsAsync(
        BlokeBotDbContext db,
        DateTime expiresAtOrBeforeUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.ExecuteDialectAsync(
            db,
            $"DELETE FROM automation_event_receipts WHERE ExpiresAtUtc <= {expiresAtOrBeforeUtc};",
            $"DELETE FROM automation_event_receipts WHERE \"ExpiresAtUtc\" <= {expiresAtOrBeforeUtc};",
            cancellationToken
        );

    public static Task<int> TryClaimAutomationEventReceiptAsync(
        BlokeBotDbContext db,
        int hostId,
        string sourceDefinitionId,
        string providerMessageId,
        DateTime claimedAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.InsertIgnoreAsync(
            db,
            $"""
            INSERT OR IGNORE INTO automation_event_receipts
                ("HostId", "SourceDefinitionId", "ProviderMessageId", "ClaimedAtUtc", "ExpiresAtUtc")
            VALUES
                ({hostId}, {sourceDefinitionId}, {providerMessageId}, {claimedAtUtc}, {expiresAtUtc});
            """,
            $"""
            INSERT INTO automation_event_receipts
                ("HostId", "SourceDefinitionId", "ProviderMessageId", "ClaimedAtUtc", "ExpiresAtUtc")
            VALUES
                ({hostId}, {sourceDefinitionId}, {providerMessageId}, {claimedAtUtc}, {expiresAtUtc})
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken
        );
}
