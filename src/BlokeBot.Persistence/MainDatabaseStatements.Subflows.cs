using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed record AutomationSubflowLibraryRow(
    Guid Id,
    Guid SubflowId,
    int Revision,
    string Name,
    string Description
);

public static partial class MainDatabaseStatements
{
    public static IQueryable<AutomationSubflowLibraryRow> QuerySubflowLibrary(
        BlokeBotDbContext db,
        int hostId
    ) =>
        db.Database.Provider() == BlokeBotDatabaseProvider.PostgreSql
            ? db.Database.SqlQuery<AutomationSubflowLibraryRow>(
                $"""
                SELECT r."Id", r."SubflowId", r."Revision",
                       "SnapshotJson"::jsonb ->> 'name' AS "Name",
                       "SnapshotJson"::jsonb ->> 'description' AS "Description"
                FROM automation_subflow_revisions r
                JOIN automation_subflows s ON s."HostId" = r."HostId" AND s."Id" = r."SubflowId"
                    AND s."LastRevision" = r."Revision"
                WHERE r."HostId" = {hostId}
                """
            )
            : db.Database.SqlQuery<AutomationSubflowLibraryRow>(
                $"""
                SELECT r."Id", r."SubflowId", r."Revision",
                       json_extract("SnapshotJson", '$.name') AS "Name",
                       json_extract("SnapshotJson", '$.description') AS "Description"
                FROM automation_subflow_revisions r
                JOIN automation_subflows s ON s."HostId" = r."HostId" AND s."Id" = r."SubflowId"
                    AND s."LastRevision" = r."Revision"
                WHERE r."HostId" = {hostId}
                """
            );
}
