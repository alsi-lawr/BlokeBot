using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Admin.HostedChannels;

public sealed class HostedChannelDirectoryService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<IReadOnlyList<HostedChannelAdminView>> LoadHostedChannelsAsync(
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => new
            {
                x.Id,
                x.Login,
                x.DisplayName,
                x.ProfileImageUrl,
                IsChannelBotAuthorized = x.ChannelBotAuthorizedAtUtc != null,
                x.BotRuntimeState,
                x.BotRuntimeStateChangedAtUtc,
            })
            .ToArrayAsync(ct);
        return hosts
            .Select(x => new HostedChannelAdminView(
                x.Id,
                x.Login,
                x.DisplayName,
                x.ProfileImageUrl,
                x.IsChannelBotAuthorized,
                HostedChannelRuntimeLifecycle.FromPersistence(
                    x.BotRuntimeState,
                    x.BotRuntimeStateChangedAtUtc
                )
            ))
            .ToArray();
    }

    public async Task<IReadOnlySet<int>> LoadHostedChannelIdsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.Hosts.AsNoTracking().Select(x => x.Id).ToArrayAsync(ct);
        return ids.ToHashSet();
    }

    public IO<Option<BotHostChoice>, Never> LoadHostChoice(int hostId, AuthRole role) =>
        IO<Option<BotHostChoice>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db
                .Hosts.AsNoTracking()
                .Where(x => x.Id == hostId)
                .Select(x => new BotHostChoice(
                    x.Id,
                    x.Login,
                    x.DisplayName,
                    role,
                    x.ProfileImageUrl
                ))
                .SingleOrDefaultAsync(ct);
            return Result<Option<BotHostChoice>, Never>.Success(
                Option<BotHostChoice>.FromNullable(host)
            );
        });

    public async Task<IReadOnlyList<BotHostChoice>> LoadExistingHostChoicesAsync(
        IReadOnlyList<BotHostChoice> choices,
        CancellationToken ct
    )
    {
        if (choices.Count == 0)
        {
            return [];
        }

        var roles = choices.ToDictionary(x => x.Id, x => x.Role);
        var ids = roles.Keys.ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.DisplayName)
            .Select(x => new
            {
                x.Id,
                x.Login,
                x.DisplayName,
                x.ProfileImageUrl,
            })
            .ToArrayAsync(ct);

        return hosts
            .Where(x => roles.ContainsKey(x.Id))
            .Select(x => new BotHostChoice(
                x.Id,
                x.Login,
                x.DisplayName,
                roles[x.Id],
                x.ProfileImageUrl
            ))
            .ToArray();
    }
}
