using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public static partial class MainDatabaseStatements
{
    public static Task<Guid[]> PluginAutomationFlowIdsAsync(
        BlokeBotDbContext db,
        string pluginId,
        CancellationToken cancellationToken
    ) =>
        db.Database.Provider() switch
        {
            BlokeBotDatabaseProvider.Sqlite => db
                .AutomationFlows.FromSqlInterpolated(
                    $"""
                    SELECT DISTINCT flow.*
                    FROM automation_flows AS flow
                    LEFT JOIN automation_flow_nodes AS node ON node."FlowId" = flow."Id"
                    WHERE (
                        node."PluginProvenanceJson" IS NOT NULL
                        AND json_valid(node."PluginProvenanceJson")
                        AND json_extract(node."PluginProvenanceJson", '$.pluginId') = {pluginId}
                    ) OR EXISTS (
                        SELECT 1
                        FROM plugin_automation_instantiations AS ledger
                        WHERE ledger."FlowId" = flow."Id" AND ledger."PluginId" = {pluginId}
                    )
                    """
                )
                .Select(static flow => flow.Id)
                .ToArrayAsync(cancellationToken),
            BlokeBotDatabaseProvider.PostgreSql => db
                .AutomationFlows.FromSqlInterpolated(
                    $"""
                    SELECT DISTINCT flow.*
                    FROM automation_flows AS flow
                    LEFT JOIN automation_flow_nodes AS node ON node."FlowId" = flow."Id"
                    WHERE (
                        node."PluginProvenanceJson" IS NOT NULL
                        AND CASE
                            WHEN pg_input_is_valid(node."PluginProvenanceJson", 'jsonb')
                            THEN (node."PluginProvenanceJson"::jsonb)->>'pluginId' = {pluginId}
                            ELSE FALSE
                        END
                    ) OR EXISTS (
                        SELECT 1
                        FROM plugin_automation_instantiations AS ledger
                        WHERE ledger."FlowId" = flow."Id" AND ledger."PluginId" = {pluginId}
                    )
                    """
                )
                .Select(static flow => flow.Id)
                .ToArrayAsync(cancellationToken),
        };
}
