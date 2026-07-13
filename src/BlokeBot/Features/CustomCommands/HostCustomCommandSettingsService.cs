using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.CustomCommands;

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

    public async Task SetTimeZoneIdAsync(int hostId, string timeZoneId, CancellationToken ct)
    {
        var normalized = NormalizeTimeZoneId(timeZoneId);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        if (host.TimeZoneId == normalized)
        {
            return;
        }

        host.TimeZoneId = normalized;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.CustomCommandsChanged, ct);
    }

    public static string NormalizeTimeZoneId(string timeZoneId)
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return normalized;
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Time zone '{normalized}' was not found.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"Time zone '{normalized}' is invalid.", ex);
        }
    }
}
