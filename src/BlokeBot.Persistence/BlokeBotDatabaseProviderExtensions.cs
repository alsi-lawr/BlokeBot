using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BlokeBot.Persistence;

internal static class BlokeBotDatabaseProviderExtensions
{
    internal static BlokeBotDatabaseProvider Provider(this DatabaseFacade database) =>
        database.ProviderName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => BlokeBotDatabaseProvider.Sqlite,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => BlokeBotDatabaseProvider.PostgreSql,
            _ => throw new InvalidOperationException("The main database provider is unsupported."),
        };
}
