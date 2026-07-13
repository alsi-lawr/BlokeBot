using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Hosts;

public sealed class BotHostProvisioningService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes,
    IEnumerable<IBotHostSeeder> seeders
)
{
    public async Task<int> EnsureHostAsync(
        string login,
        string? twitchUserId,
        string? displayName,
        string? profileImageUrl,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalized = LoginName.Parse(login);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Login == normalized.Value, ct);
        if (host is null)
        {
            host = new BotHost
            {
                CreatedAtUtc = DateTime.UtcNow,
                DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? normalized.Value
                    : displayName.Trim(),
                Login = normalized.Value,
                ProfileImageUrl = string.IsNullOrWhiteSpace(profileImageUrl)
                    ? null
                    : profileImageUrl.Trim(),
                TwitchUserId = twitchUserId,
            };
            db.Hosts.Add(host);
        }
        else
        {
            host.DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? host.DisplayName
                : displayName.Trim();
            host.ProfileImageUrl = string.IsNullOrWhiteSpace(profileImageUrl)
                ? host.ProfileImageUrl
                : profileImageUrl.Trim();
            host.TwitchUserId = string.IsNullOrWhiteSpace(twitchUserId)
                ? host.TwitchUserId
                : twitchUserId;
        }

        await db.SaveChangesAsync(ct);
        if (!await db.HostModAccessSettings.AnyAsync(x => x.HostId == host.Id, ct))
        {
            db.HostModAccessSettings.Add(
                new HostModAccessSettings
                {
                    HostId = host.Id,
                    ModsEnabled = true,
                    AllowModsByDefault = true,
                }
            );
            await db.SaveChangesAsync(ct);
        }

        foreach (var seeder in seeders)
        {
            await seeder.SeedAsync(host.Id, ct);
        }

        await changes.NotifyChangedAsync(ct);
        return host.Id;
    }
}
