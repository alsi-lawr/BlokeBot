using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeStatusService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ChannelBotAuthorizationService channelBotAuthorization,
    HostBotStatusService botStatus
)
{
    public async Task<IReadOnlyList<string>> LoadConnectableChannelLoginsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(host =>
                host.ChannelBotAuthorizedAtUtc != null
                && (
                    host.BotRuntimeState == BotChannelRuntimeState.Starting
                    || host.BotRuntimeState == BotChannelRuntimeState.Started
                )
            )
            .OrderBy(host => host.Login)
            .Select(host => new
            {
                host.Login,
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes,
            })
            .ToArrayAsync(ct);

        return hosts
            .Where(host =>
                channelBotAuthorization.IsCurrent(
                    host.ChannelBotAuthorizedAtUtc,
                    host.ChannelBotAuthorizedScopes
                )
            )
            .Select(host => host.Login)
            .ToArray();
    }

    public async Task<HostedChannelRuntimeStatus?> LoadHostStatusAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var host = await LoadHostRuntimeFieldsAsync(hostId, ct);
        if (host is null)
            return null;

        return new HostedChannelRuntimeStatus(
            host.ChannelBotAuthorizedAtUtc != null,
            channelBotAuthorization.IsCurrent(
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes
            ),
            await botStatus.GetStatusAsync(host.Login, ct),
            host.BotRuntimeState
        );
    }

    public async Task<HostedChannelRuntimeSummary?> LoadHostRuntimeSummaryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var host = await LoadHostRuntimeFieldsAsync(hostId, ct);
        if (host is null)
            return null;

        return new HostedChannelRuntimeSummary(
            host.ChannelBotAuthorizedAtUtc != null,
            channelBotAuthorization.IsCurrent(
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes
            ),
            host.BotRuntimeState
        );
    }

    private async Task<HostRuntimeFields?> LoadHostRuntimeFieldsAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == hostId)
            .Select(x => new HostRuntimeFields(
                x.ChannelBotAuthorizedAtUtc,
                x.ChannelBotAuthorizedScopes,
                x.BotRuntimeState,
                x.Login
            ))
            .SingleOrDefaultAsync(ct);
    }

    private sealed record HostRuntimeFields(
        DateTime? ChannelBotAuthorizedAtUtc,
        string? ChannelBotAuthorizedScopes,
        BotChannelRuntimeState BotRuntimeState,
        string Login
    );
}
