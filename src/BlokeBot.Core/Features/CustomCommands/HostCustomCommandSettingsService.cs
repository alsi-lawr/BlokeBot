using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class HostCustomCommandSettingsService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events
)
{
    public async Task<string> GetTimeZoneIdAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
                .Hosts.AsNoTracking()
                .Where(x => x.Id == hostId)
                .Select(x => x.TimeZoneId)
                .SingleOrDefaultAsync(ct)
            ?? "UTC";
    }

    public async Task SetTimeZoneAsync(
        int hostId,
        CustomCommandTimeZone timeZone,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        if (host.TimeZoneId == timeZone.Id)
        {
            return;
        }

        host.TimeZoneId = timeZone.Id;
        _ = await db.SaveChangesAsync(ct);
        _ = await events.PublishAsync(AppEventKind.CustomCommandsChanged, ct);
    }
}
