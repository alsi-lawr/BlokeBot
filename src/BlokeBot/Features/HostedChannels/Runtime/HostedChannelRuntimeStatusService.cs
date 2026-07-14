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
            .Where(host => host.ChannelBotAuthorizedAtUtc != null)
            .OrderBy(host => host.Login)
            .Select(host => new
            {
                host.Login,
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes,
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc,
            })
            .ToArrayAsync(ct);

        return hosts
            .Select(host => new
            {
                Host = host,
                Lifecycle = HostedChannelRuntimeLifecycle.FromPersistence(
                    host.BotRuntimeState,
                    host.BotRuntimeStateChangedAtUtc
                ),
            })
            .Where(host =>
                host.Lifecycle
                    is HostedChannelRuntimeLifecycle.Starting
                        or HostedChannelRuntimeLifecycle.Started
                && channelBotAuthorization.IsCurrent(
                    host.Host.ChannelBotAuthorizedAtUtc,
                    host.Host.ChannelBotAuthorizedScopes
                )
            )
            .Select(host => host.Host.Login)
            .ToArray();
    }

    public async Task<HostedChannelRuntimeStatus?> LoadHostStatusAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var host = await LoadHostRuntimeFieldsAsync(hostId, ct);
        if (host is null)
        {
            return null;
        }

        return new HostedChannelRuntimeStatus(
            host.ChannelBotAuthorizedAtUtc != null,
            channelBotAuthorization.IsCurrent(
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes
            ),
            await botStatus.GetStatusAsync(host.Login, ct),
            HostedChannelRuntimeLifecycle.FromPersistence(
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc
            )
        );
    }

    public async Task<HostedChannelRuntimeSummary?> LoadHostRuntimeSummaryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var host = await LoadHostRuntimeFieldsAsync(hostId, ct);
        if (host is null)
        {
            return null;
        }

        return new HostedChannelRuntimeSummary(
            host.ChannelBotAuthorizedAtUtc != null,
            channelBotAuthorization.IsCurrent(
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes
            ),
            HostedChannelRuntimeLifecycle.FromPersistence(
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc
            )
        );
    }

    private async Task<HostRuntimeFields?> LoadHostRuntimeFieldsAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == hostId)
            .Select(x => new HostRuntimeFields(
                x.ChannelBotAuthorizedAtUtc,
                x.ChannelBotAuthorizedScopes,
                x.BotRuntimeState,
                x.BotRuntimeStateChangedAtUtc,
                x.Login
            ))
            .SingleOrDefaultAsync(ct);
    }

    private sealed record HostRuntimeFields(
        DateTime? ChannelBotAuthorizedAtUtc,
        string? ChannelBotAuthorizedScopes,
        BotChannelRuntimeState BotRuntimeState,
        DateTime? BotRuntimeStateChangedAtUtc,
        string Login
    );
}
