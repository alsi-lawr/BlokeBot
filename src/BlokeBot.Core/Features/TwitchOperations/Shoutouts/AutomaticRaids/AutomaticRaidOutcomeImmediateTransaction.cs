using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

internal sealed class AutomaticRaidOutcomeImmediateTransaction(
    SqliteTransaction providerTransaction,
    IDbContextTransaction contextTransaction
) : IAsyncDisposable
{
    internal static async Task<AutomaticRaidOutcomeImmediateTransaction> StartAsync(
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection =
            db.Database.GetDbConnection() as SqliteConnection
            ?? throw new InvalidOperationException(
                "Automatic raid outcome persistence requires SQLite."
            );
        var providerTransaction = connection.BeginTransaction(deferred: false);
        try
        {
            var contextTransaction =
                await db.Database.UseTransactionAsync(providerTransaction, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The automatic raid outcome transaction could not be attached."
                );
            return new AutomaticRaidOutcomeImmediateTransaction(
                providerTransaction,
                contextTransaction
            );
        }
        catch
        {
            await providerTransaction.DisposeAsync();
            throw;
        }
    }

    internal Task CommitAsync(CancellationToken cancellationToken) =>
        contextTransaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await contextTransaction.DisposeAsync();
        await providerTransaction.DisposeAsync();
    }
}
