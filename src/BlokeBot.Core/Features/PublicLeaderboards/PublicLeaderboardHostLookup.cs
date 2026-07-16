using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PublicLeaderboards;

public sealed class PublicLeaderboardHostLookup(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public IO<Option<PublicLeaderboardHost>, Never> Find(string channel)
    {
        var login = LoginName.Parse(channel).Value;
        if (string.IsNullOrWhiteSpace(login))
        {
            return IO<Option<PublicLeaderboardHost>, Never>.Create(_ =>
                ValueTask.FromResult(
                    Result<Option<PublicLeaderboardHost>, Never>.Success(
                        Option<PublicLeaderboardHost>.None
                    )
                )
            );
        }

        return IO<Option<PublicLeaderboardHost>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db
                .Hosts.AsNoTracking()
                .Where(x => x.Login == login)
                .Select(x => new PublicLeaderboardHost(
                    x.Id,
                    x.Login,
                    x.DisplayName,
                    x.EnabledFeatures
                ))
                .SingleOrDefaultAsync(ct);
            return Result<Option<PublicLeaderboardHost>, Never>.Success(
                Option<PublicLeaderboardHost>.FromNullable(host)
            );
        });
    }
}
