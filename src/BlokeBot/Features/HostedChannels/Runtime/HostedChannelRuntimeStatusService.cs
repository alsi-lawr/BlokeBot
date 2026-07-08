using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeStatusService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IConfiguration configuration,
    HostBotStatusService botStatus
)
{
    public async Task<IReadOnlyList<string>> LoadConnectableChannelLoginsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(host =>
                host.ChannelBotAuthorizedAtUtc != null
                && (
                    host.BotRuntimeState == BotChannelRuntimeState.Starting
                    || host.BotRuntimeState == BotChannelRuntimeState.Started
                )
            )
            .OrderBy(host => host.Login)
            .Select(host => host.Login)
            .ToArrayAsync(ct);
    }

    public async Task<HostedChannelRuntimeStatus?> LoadHostStatusAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var requiredChannelScopes = FormatScopes(
            configuration.GetSection("TwitchBot:ChannelAuthorization:Scopes").Get<string[]>() ?? []
        );
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == hostId)
            .Select(x => new
            {
                x.ChannelBotAuthorizedAtUtc,
                x.ChannelBotAuthorizedScopes,
                x.BotRuntimeState,
                x.Login,
            })
            .SingleOrDefaultAsync(ct);
        if (host is null)
            return null;

        return new HostedChannelRuntimeStatus(
            host.ChannelBotAuthorizedAtUtc != null,
            host.ChannelBotAuthorizedAtUtc != null
                && (host.ChannelBotAuthorizedScopes ?? string.Empty) == requiredChannelScopes,
            await botStatus.GetStatusAsync(host.Login, ct),
            host.BotRuntimeState
        );
    }

    private static string FormatScopes(IEnumerable<string> scopes) =>
        string.Join(
            ' ',
            scopes
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        );
}
