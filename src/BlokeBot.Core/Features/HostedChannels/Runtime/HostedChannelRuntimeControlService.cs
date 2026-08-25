using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeControlService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ChannelBotAuthorizationService channelBotAuthorization,
    HostBotAccountAuthorizationService botAccounts,
    IOptions<BlokeBotOptions> options,
    HostedChannelRuntimeTransitionService runtimeTransitions
)
{
    private TimeSpan _runtimeChangeCooldown =>
        TimeSpan.FromSeconds(Math.Max(0, options.Value.BotStateChangeCooldownSeconds));

    public IO<HostedChannelRuntimeControlOutcome, Never> Start(int hostId) =>
        IO<HostedChannelRuntimeControlOutcome, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
            if (host is null)
            {
                return Success(new HostedChannelRuntimeControlOutcome.HostNotFound());
            }

            _ = HostedChannelRuntimeLifecycle.FromPersistence(
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc
            );

            if (
                !channelBotAuthorization.IsCurrent(
                    host.ChannelBotAuthorizedAtUtc,
                    host.ChannelBotAuthorizedScopes
                )
            )
            {
                return Success(
                    new HostedChannelRuntimeControlOutcome.ChannelAuthorizationRequired()
                );
            }

            var botAccountStatus = await botAccounts.GetStatusAsync(host.Id, ct);
            if (
                botAccountStatus.State
                is not BotAccountAuthorizationState.Disabled
                    and not BotAccountAuthorizationState.Ready
            )
            {
                return Success(new HostedChannelRuntimeControlOutcome.CustomBotNotReady());
            }

            if (CooldownMessage(host) is { } cooldown)
            {
                return Success(cooldown);
            }

            var transition = await runtimeTransitions.RequestStartAsync(host.Id, ct);
            return transition is HostedChannelRuntimeTransitionOutcome.HostNotFound
                ? Success(new HostedChannelRuntimeControlOutcome.HostNotFound())
                : Success(new HostedChannelRuntimeControlOutcome.Accepted());
        });

    public IO<HostedChannelRuntimeControlOutcome, Never> Stop(int hostId) =>
        IO<HostedChannelRuntimeControlOutcome, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
            if (host is null)
            {
                return Success(new HostedChannelRuntimeControlOutcome.HostNotFound());
            }

            _ = HostedChannelRuntimeLifecycle.FromPersistence(
                host.BotRuntimeState,
                host.BotRuntimeStateChangedAtUtc
            );

            if (CooldownMessage(host) is { } cooldown)
            {
                return Success(cooldown);
            }

            var transition = await runtimeTransitions.RequestStopAsync(host.Id, ct);
            return transition is HostedChannelRuntimeTransitionOutcome.HostNotFound
                ? Success(new HostedChannelRuntimeControlOutcome.HostNotFound())
                : Success(new HostedChannelRuntimeControlOutcome.Accepted());
        });

    private HostedChannelRuntimeControlOutcome.Cooldown? CooldownMessage(BotHost host)
    {
        if (host.BotRuntimeStateChangedAtUtc is not { } changedAt)
        {
            return null;
        }

        if (_runtimeChangeCooldown == TimeSpan.Zero)
        {
            return null;
        }

        var nextAllowedAt = changedAt.Add(_runtimeChangeCooldown);
        return nextAllowedAt > DateTime.UtcNow
            ? new HostedChannelRuntimeControlOutcome.Cooldown(nextAllowedAt)
            : null;
    }

    private static Result<HostedChannelRuntimeControlOutcome, Never> Success(
        HostedChannelRuntimeControlOutcome outcome
    ) => Result<HostedChannelRuntimeControlOutcome, Never>.Success(outcome);
}

public abstract record HostedChannelRuntimeControlOutcome
{
    private HostedChannelRuntimeControlOutcome() { }

    public sealed record Accepted : HostedChannelRuntimeControlOutcome;

    public sealed record HostNotFound : HostedChannelRuntimeControlOutcome;

    public sealed record ChannelAuthorizationRequired : HostedChannelRuntimeControlOutcome;

    public sealed record CustomBotNotReady : HostedChannelRuntimeControlOutcome;

    public sealed record Cooldown(DateTime NextAllowedAtUtc) : HostedChannelRuntimeControlOutcome;
}
