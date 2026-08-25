using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeStatusService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ChannelBotAuthorizationService channelBotAuthorization,
    HostedChannelRuntimeTransitionService runtimeTransitions
)
{
    public async Task<IReadOnlyList<BotChannelTarget>> LoadConnectableChannelTargetsAsync(
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(host => host.ChannelBotAuthorizedAtUtc != null)
            .OrderBy(host => host.Login)
            .Select(host => new
            {
                host.Id,
                host.Login,
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes,
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc,
            })
            .ToArrayAsync(ct);

        var connectable = hosts
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
            .ToArray();
        return await Task.WhenAll(
            connectable.Select(host =>
                runtimeTransitions.GetOrCreateSessionTargetAsync(host.Host.Id, host.Host.Login, ct)
            )
        );
    }

    public IO<Option<HostedChannelRuntimeSummary>, Never> LoadHostRuntimeSummary(int hostId) =>
        IO<Option<HostedChannelRuntimeSummary>, Never>.Create(async ct =>
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

    private IO<Option<HostRuntimeFields>, Never> LoadHostRuntimeFields(int hostId) =>
        IO<Option<HostRuntimeFields>, Never>.Create(async ct =>
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

    private sealed record HostRuntimeFields(
        DateTime? ChannelBotAuthorizedAtUtc,
        string? ChannelBotAuthorizedScopes,
        BotChannelRuntimeState BotRuntimeState,
        DateTime? BotRuntimeStateChangedAtUtc,
        string Login
    );
}
