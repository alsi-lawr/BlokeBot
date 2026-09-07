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
                SELECT "Id", "SubflowId", "Revision",
                       "SnapshotJson"::jsonb ->> 'name' AS "Name",
                       "SnapshotJson"::jsonb ->> 'description' AS "Description"
                FROM automation_subflow_revisions WHERE "HostId" = {hostId}
                """
            )
            : db.Database.SqlQuery<AutomationSubflowLibraryRow>(
                $"""
                SELECT "Id", "SubflowId", "Revision",
                       json_extract("SnapshotJson", '$.name') AS "Name",
                       json_extract("SnapshotJson", '$.description') AS "Description"
                FROM automation_subflow_revisions WHERE "HostId" = {hostId}
                """
            );
}
