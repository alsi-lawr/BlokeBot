using System.Data.Common;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseCutover;

internal sealed class CutoverDbContextFactory(BlokeBotDatabaseConfiguration configuration)
    : IDbContextFactory<BlokeBotDbContext>
{
    public BlokeBotDbContext CreateDbContext() => CreateDbContext(configuration);

    // A pooled connection outlives its DbContext and would count as a foreign target session.
    internal static BlokeBotDbContext CreateDbContext(BlokeBotDatabaseConfiguration configuration)
    {
        var db = configuration.CreateDbContext();
        var connection = db.Database.GetDbConnection();
        var settings = new DbConnectionStringBuilder
        {
            ConnectionString = connection.ConnectionString,
        };
        settings["Pooling"] = false;
        connection.ConnectionString = settings.ConnectionString;
        return db;
    }
}
