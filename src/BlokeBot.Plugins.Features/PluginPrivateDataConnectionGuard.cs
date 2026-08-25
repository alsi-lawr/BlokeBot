using BlokeBot.Plugins.Contracts;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace BlokeBot.Plugins.Features;

internal enum PluginPrivateStatementValidation
{
    Allowed,
    PrepareFailed,
    Restricted,
}

internal static class PluginPrivateDataConnectionGuard
{
    private static readonly strdelegate_authorizer _authorize = Authorize;

    internal static void Apply(SqliteConnection connection)
    {
        var handle = connection.Handle;
        if (raw.sqlite3_set_authorizer(handle, _authorize, user_data: null!) != raw.SQLITE_OK)
        {
            throw new InvalidOperationException(
                "The plugin private-data connection could not be isolated."
            );
        }

        _ = raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_ATTACHED, 0);
        _ = raw.sqlite3_limit(
            handle,
            raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            PluginContractLimits.MaximumSqlParameters
        );
    }

    internal static PluginPrivateStatementValidation ValidateStatement(
        SqliteConnection connection,
        string sql
    )
    {
        var remaining = sql;
        var statements = 0;
        while (remaining.Length > 0)
        {
            sqlite3_stmt? statement = null;
            try
            {
                var result = raw.sqlite3_prepare_v3(
                    connection.Handle,
                    remaining,
                    raw.SQLITE_PREPARE_NO_VTAB,
                    out statement,
                    out var tail
                );
                if (result != raw.SQLITE_OK)
                {
                    return
                        result == raw.SQLITE_AUTH
                        || statements > 0
                        || CanPrepareWithVirtualTables(connection, remaining)
                        ? PluginPrivateStatementValidation.Restricted
                        : PluginPrivateStatementValidation.PrepareFailed;
                }
                if (statement is not null && ++statements > 1)
                {
                    return PluginPrivateStatementValidation.Restricted;
                }
                if (tail.Length == remaining.Length)
                {
                    return PluginPrivateStatementValidation.Restricted;
                }
                remaining = tail;
            }
            finally
            {
                statement?.Dispose();
            }
        }

        return statements == 1
            ? PluginPrivateStatementValidation.Allowed
            : PluginPrivateStatementValidation.Restricted;
    }

    private static bool CanPrepareWithVirtualTables(SqliteConnection connection, string sql)
    {
        sqlite3_stmt? statement = null;
        try
        {
            return raw.sqlite3_prepare_v3(connection.Handle, sql, 0, out statement) == raw.SQLITE_OK
                && statement is not null;
        }
        finally
        {
            statement?.Dispose();
        }
    }

    private static int Authorize(
        object userData,
        int action,
        string? first,
        string? second,
        string? database,
        string? source
    ) =>
        action
            is raw.SQLITE_ATTACH
                or raw.SQLITE_DETACH
                or raw.SQLITE_PRAGMA
                or raw.SQLITE_CREATE_VTABLE
                or raw.SQLITE_DROP_VTABLE
        || !AllowedDatabase(database)
        || (action == raw.SQLITE_ALTER_TABLE && !AllowedDatabase(first))
            ? raw.SQLITE_DENY
            : raw.SQLITE_OK;

    private static bool AllowedDatabase(string? database) => database is null or "main" or "temp";
}
