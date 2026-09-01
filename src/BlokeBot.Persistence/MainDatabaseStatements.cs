using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public static partial class MainDatabaseStatements
{
    public static Task<int> LockHostAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        MainDatabaseStatements.ExecuteDialectAsync(
            db,
            $"UPDATE hosts SET EnabledFeatures = EnabledFeatures WHERE Id = {hostId};",
            $"UPDATE hosts SET \"EnabledFeatures\" = \"EnabledFeatures\" WHERE \"Id\" = {hostId};",
            cancellationToken
        );

    private static Task<int> InsertIgnoreAsync(
        BlokeBotDbContext db,
        FormattableString sqlite,
        FormattableString postgreSql,
        CancellationToken cancellationToken
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            db.Database.Provider() == BlokeBotDatabaseProvider.PostgreSql ? postgreSql : sqlite,
            cancellationToken
        );

    private static Task<int> ExecuteDialectAsync(
        BlokeBotDbContext db,
        FormattableString sqlite,
        FormattableString postgreSql,
        CancellationToken cancellationToken
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            db.Database.Provider() == BlokeBotDatabaseProvider.PostgreSql ? postgreSql : sqlite,
            cancellationToken
        );
}
