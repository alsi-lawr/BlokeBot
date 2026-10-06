using System.Data.Common;
using System.Globalization;
using System.Numerics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Persistence;

public static partial class MainDatabaseStatements
{
    public static async Task EnsurePointBalanceAsync(
        BlokeBotDbContext db,
        PointBalanceTarget target,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        await using var command = PointCommand(
            db,
            """
            INSERT INTO point_balances ("HostId", "Login", "Amount", "UpdatedAtUtc")
            VALUES (@host, @login, '0', @now) ON CONFLICT ("HostId", "Login") DO NOTHING;
            """,
            target
        );
        AddPointParameter(command, "now", nowUtc);
        _ = await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<PointDeltaSqlOutcome> ApplyPointDeltaAsync(
        BlokeBotDbContext db,
        PointBalanceTarget target,
        CanonicalPointInteger signedDelta,
        PointDeltaBounds bounds,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var postgres = db.Database.Provider() == BlokeBotDatabaseProvider.PostgreSql;
        var sum = postgres
            ? "(\"Amount\"::numeric + CAST(@delta AS numeric))"
            : "blokebot_point_add(\"Amount\", @delta)";
        var before = postgres
            ? "(\"Amount\"::numeric - CAST(@delta AS numeric))::text"
            : "blokebot_point_add(\"Amount\", @negativeDelta)";
        var admission = postgres
            ? $"\"Amount\"::numeric >= CAST(@minimum AS numeric) AND {sum} BETWEEN 0 AND CAST(@maximum AS numeric)"
            : $"blokebot_point_compare(\"Amount\", @minimum) >= 0 AND blokebot_point_compare({sum}, '0') >= 0 AND blokebot_point_compare({sum}, @maximum) <= 0";
        await using var command = PointCommand(
            db,
            $"""
            UPDATE point_balances SET "Amount" = {sum}{(
                postgres ? "::text" : ""
            )}, "UpdatedAtUtc" = @now
            WHERE "HostId" = @host AND "Login" = @login AND {admission}
            RETURNING {before}, "Amount";
            """,
            target
        );
        AddPointParameter(command, "delta", signedDelta.Text);
        AddPointParameter(
            command,
            "negativeDelta",
            CanonicalPointInteger.From(-signedDelta.ToBigInteger()).Text
        );
        AddPointParameter(command, "minimum", bounds.MinimumCurrent.Text);
        AddPointParameter(command, "maximum", bounds.MaximumAfter.Text);
        AddPointParameter(command, "now", nowUtc);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                return new PointDeltaSqlOutcome.Applied(
                    ReadPointInteger(reader, 0),
                    ReadPointInteger(reader, 1)
                );
            }
        }
        return new PointDeltaSqlOutcome.Rejected(await ReadPointBalanceAsync(db, target, ct));
    }

    public static async Task<PointDeltaSqlOutcome> ApplyCreatingPointCreditAsync(
        BlokeBotDbContext db,
        PointBalanceTarget target,
        CanonicalPointInteger nonnegativeCredit,
        CanonicalPointInteger maximumAfter,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var postgres = db.Database.Provider() == BlokeBotDatabaseProvider.PostgreSql;
        var sum = postgres
            ? "(point_balances.\"Amount\"::numeric + CAST(@delta AS numeric))"
            : "blokebot_point_add(point_balances.\"Amount\", @delta)";
        var before = postgres
            ? "(\"Amount\"::numeric - CAST(@delta AS numeric))::text"
            : "blokebot_point_add(\"Amount\", @negativeDelta)";
        var initial = postgres
            ? "CAST(@delta AS numeric) >= 0 AND CAST(@delta AS numeric) <= CAST(@maximum AS numeric)"
            : "blokebot_point_compare(@delta, '0') >= 0 AND blokebot_point_compare(@delta, @maximum) <= 0";
        var admission = postgres
            ? $"{sum} BETWEEN 0 AND CAST(@maximum AS numeric)"
            : $"blokebot_point_compare({sum}, '0') >= 0 AND blokebot_point_compare({sum}, @maximum) <= 0";
        await using var command = PointCommand(
            db,
            $"""
            INSERT INTO point_balances ("HostId", "Login", "Amount", "UpdatedAtUtc")
            SELECT @host, @login, @delta, @now WHERE {initial}
            ON CONFLICT ("HostId", "Login") DO UPDATE SET "Amount" = {sum}{(
                postgres ? "::text" : ""
            )}, "UpdatedAtUtc" = @now
            WHERE {admission} RETURNING {before}, "Amount";
            """,
            target
        );
        AddPointParameter(command, "delta", nonnegativeCredit.Text);
        AddPointParameter(
            command,
            "negativeDelta",
            CanonicalPointInteger.From(-nonnegativeCredit.ToBigInteger()).Text
        );
        AddPointParameter(command, "maximum", maximumAfter.Text);
        AddPointParameter(command, "now", nowUtc);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                return new PointDeltaSqlOutcome.Applied(
                    ReadPointInteger(reader, 0),
                    ReadPointInteger(reader, 1)
                );
            }
        }
        return new PointDeltaSqlOutcome.Rejected(await ReadPointBalanceAsync(db, target, ct));
    }

    public static async Task<PointDeleteSqlOutcome> DeletePointBalanceAsync(
        BlokeBotDbContext db,
        PointBalanceTarget target,
        CancellationToken ct
    )
    {
        await using var command = PointCommand(
            db,
            "DELETE FROM point_balances WHERE \"HostId\" = @host AND \"Login\" = @login RETURNING \"Amount\";",
            target
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new PointDeleteSqlOutcome.Deleted(ReadPointInteger(reader, 0))
            : new PointDeleteSqlOutcome.Missing();
    }

    public static async Task<WatchTimeLedgerInsertOutcome> TryAppendWatchTimeLedgerAsync(
        BlokeBotDbContext db,
        WatchTimeLedgerWrite entry,
        CancellationToken ct
    )
    {
        await using var command = PointCommand(
            db,
            """
            INSERT INTO point_ledger_entries ("HostId", "CreatedAtUtc", "Kind", "Login", "Delta", "BalanceAfter", "Note", "OperationKey")
            VALUES (@host, @now, 'WatchTimeReward', @login, @delta, @after, '', @key)
            ON CONFLICT ("HostId", "OperationKey") DO NOTHING RETURNING "Id";
            """,
            entry.Target
        );
        AddPointParameter(command, "now", entry.CreatedAtUtc);
        AddPointParameter(command, "delta", entry.Delta.Text);
        AddPointParameter(command, "after", entry.BalanceAfter.Text);
        AddPointParameter(command, "key", entry.OperationKey);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new WatchTimeLedgerInsertOutcome.Inserted()
            : new WatchTimeLedgerInsertOutcome.ExistingKey();
    }

    private static async Task<PointBalanceRead> ReadPointBalanceAsync(
        BlokeBotDbContext db,
        PointBalanceTarget target,
        CancellationToken ct
    )
    {
        await using var command = PointCommand(
            db,
            "SELECT \"Amount\" FROM point_balances WHERE \"HostId\" = @host AND \"Login\" = @login;",
            target
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new PointBalanceRead.Found(ReadPointInteger(reader, 0))
            : new PointBalanceRead.Missing();
    }

    private static DbCommand PointCommand(
        BlokeBotDbContext db,
        string sql,
        PointBalanceTarget target
    )
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Point SQL requires a caller-owned transaction."
            );
        var connection = db.Database.GetDbConnection();
        if (connection is SqliteConnection sqlite)
        {
            sqlite.CreateFunction<string, string, string>(
                "blokebot_point_add",
                (a, b) =>
                    (
                        BigInteger.Parse(a, CultureInfo.InvariantCulture)
                        + BigInteger.Parse(b, CultureInfo.InvariantCulture)
                    ).ToString(CultureInfo.InvariantCulture),
                isDeterministic: true
            );
            sqlite.CreateFunction<string, string, int>(
                "blokebot_point_compare",
                (a, b) =>
                    BigInteger
                        .Parse(a, CultureInfo.InvariantCulture)
                        .CompareTo(BigInteger.Parse(b, CultureInfo.InvariantCulture)),
                isDeterministic: true
            );
        }
        var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        AddPointParameter(command, "host", target.HostId);
        AddPointParameter(command, "login", target.Login);
        return command;
    }

    private static void AddPointParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        _ = command.Parameters.Add(parameter);
    }

    private static CanonicalPointInteger ReadPointInteger(DbDataReader reader, int ordinal) =>
        CanonicalPointInteger.TryCreate(reader.GetString(ordinal), out var value)
            ? value
            : throw new PersistenceDataIntegrityException(typeof(CanonicalPointInteger));
}
