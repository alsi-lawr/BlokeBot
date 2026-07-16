using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

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

    public IO<Option<HostedChannelRuntimeStatus>, Never> LoadHostStatus(int hostId)
    {
        return IO<Option<HostedChannelRuntimeStatus>, Never>.Create(async ct =>
        {
            var fieldsResult = await LoadHostRuntimeFields(hostId).ExecuteAsync(ct);
            var fields = fieldsResult.Match(value => value, _ => throw new UnreachableException());
            return await fields.Match(
                async host =>
                {
                    var statusResult = await botStatus.GetStatus(host.Login).ExecuteAsync(ct);
                    var status = statusResult.Match(
                        value => value,
                        _ => throw new UnreachableException()
                    );
                    return Result<Option<HostedChannelRuntimeStatus>, Never>.Success(
                        Option<HostedChannelRuntimeStatus>.Some(
                            new HostedChannelRuntimeStatus(
                                host.ChannelBotAuthorizedAtUtc != null,
                                channelBotAuthorization.IsCurrent(
                                    host.ChannelBotAuthorizedAtUtc,
                                    host.ChannelBotAuthorizedScopes
                                ),
                                status,
                                HostedChannelRuntimeLifecycle.FromPersistence(
                                    host.BotRuntimeState,
                                    host.BotRuntimeStateChangedAtUtc
                                )
                            )
                        )
                    );
                },
                () =>
                    Task.FromResult(
                        Result<Option<HostedChannelRuntimeStatus>, Never>.Success(
                            Option<HostedChannelRuntimeStatus>.None
                        )
                    )
            );
        });
    }

    public IO<Option<HostedChannelRuntimeSummary>, Never> LoadHostRuntimeSummary(int hostId)
    {
        return IO<Option<HostedChannelRuntimeSummary>, Never>.Create(async ct =>
        {
            var fieldsResult = await LoadHostRuntimeFields(hostId).ExecuteAsync(ct);
            var fields = fieldsResult.Match(value => value, _ => throw new UnreachableException());
            return Result<Option<HostedChannelRuntimeSummary>, Never>.Success(
                fields.Map(host => new HostedChannelRuntimeSummary(
                    host.ChannelBotAuthorizedAtUtc != null,
                    channelBotAuthorization.IsCurrent(
                        host.ChannelBotAuthorizedAtUtc,
                        host.ChannelBotAuthorizedScopes
                    ),
                    HostedChannelRuntimeLifecycle.FromPersistence(
                        host.BotRuntimeState,
                        host.BotRuntimeStateChangedAtUtc
                    )
                ))
            );
        });
    }

    private IO<Option<HostRuntimeFields>, Never> LoadHostRuntimeFields(int hostId)
    {
        return IO<Option<HostRuntimeFields>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var fields = await db
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
            return Result<Option<HostRuntimeFields>, Never>.Success(
                Option<HostRuntimeFields>.FromNullable(fields)
            );
        });
    }

    private sealed record HostRuntimeFields(
        DateTime? ChannelBotAuthorizedAtUtc,
        string? ChannelBotAuthorizedScopes,
        BotChannelRuntimeState BotRuntimeState,
        DateTime? BotRuntimeStateChangedAtUtc,
        string Login
    );
}
