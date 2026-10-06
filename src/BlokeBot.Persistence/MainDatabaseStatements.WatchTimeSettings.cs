using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Persistence;

public sealed record WatchTimeSettingsWriteResult(
    bool Enabled,
    string? Amount,
    Guid Revision,
    Guid EnableGeneration
);

public static partial class MainDatabaseStatements
{
    public static async Task<WatchTimeSettingsWriteResult> ApplyWatchTimeSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        bool enabled,
        string? canonicalAmount,
        Guid candidateRevision,
        Guid candidateEnableGeneration,
        CancellationToken ct
    )
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Watch settings SQL requires a caller-owned transaction."
            );
        var postgres = db.Database.Provider() == BlokeBotDatabaseProvider.PostgreSql;
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        var changed = postgres
            ? "\"WatchTimePointsEnabled\" IS DISTINCT FROM @enabled OR \"WatchTimePointAmount\" IS DISTINCT FROM @amount"
            : "\"WatchTimePointsEnabled\" IS NOT @enabled OR \"WatchTimePointAmount\" IS NOT @amount";
        command.CommandText = $"""
            UPDATE points_settings SET
            "WatchTimeConfigurationRevision" = CASE WHEN {changed} THEN @revision ELSE "WatchTimeConfigurationRevision" END,
            "WatchTimeEnableGeneration" = CASE WHEN NOT @enabled THEN @empty WHEN NOT "WatchTimePointsEnabled" THEN @generation ELSE "WatchTimeEnableGeneration" END,
            "WatchTimePointsEnabled" = @enabled, "WatchTimePointAmount" = @amount
            WHERE "HostId" = @host
            RETURNING "WatchTimePointsEnabled", "WatchTimePointAmount", "WatchTimeConfigurationRevision", "WatchTimeEnableGeneration";
            """;
        AddPointParameter(command, "host", hostId);
        AddPointParameter(command, "enabled", enabled);
        AddPointParameter(
            command,
            "amount",
            canonicalAmount is null ? DBNull.Value : canonicalAmount
        );
        AddPointParameter(
            command,
            "revision",
            postgres ? candidateRevision : candidateRevision.ToString().ToUpperInvariant()
        );
        AddPointParameter(
            command,
            "generation",
            postgres
                ? candidateEnableGeneration
                : candidateEnableGeneration.ToString().ToUpperInvariant()
        );
        AddPointParameter(
            command,
            "empty",
            postgres ? Guid.Empty : Guid.Empty.ToString().ToUpperInvariant()
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(
                reader.GetBoolean(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetGuid(2),
                reader.GetGuid(3)
            )
            : throw new PersistenceDataIntegrityException(typeof(Models.PointsSettings));
    }
}
