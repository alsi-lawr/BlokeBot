using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeControlService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes,
    ChannelBotAuthorizationService channelBotAuthorization,
    IOptions<BlokeBotOptions> options
)
{
    private TimeSpan RuntimeChangeCooldown =>
        TimeSpan.FromSeconds(Math.Max(0, options.Value.BotStateChangeCooldownSeconds));

    public async Task<HostedChannelRuntimeControlResult> StartAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
            return HostedChannelRuntimeControlResult.Failure("Hosted channel was not found.");

        if (
            !channelBotAuthorization.IsCurrent(
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes
            )
        )
        {
            return HostedChannelRuntimeControlResult.Failure(
                "Authorize or reauthorize the bot on that channel before starting it."
            );
        }

        if (CooldownMessage(host) is { } cooldown)
            return cooldown;

        host.BotRuntimeState = BotChannelRuntimeState.Starting;
        host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return HostedChannelRuntimeControlResult.Success("Bot starting.");
    }

    public async Task<HostedChannelRuntimeControlResult> StopAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
            return HostedChannelRuntimeControlResult.Failure("Hosted channel was not found.");

        if (CooldownMessage(host) is { } cooldown)
            return cooldown;

        host.BotRuntimeState = host.BotRuntimeState switch
        {
            BotChannelRuntimeState.Started => BotChannelRuntimeState.Stopping,
            BotChannelRuntimeState.Stopping => BotChannelRuntimeState.Stopping,
            _ => BotChannelRuntimeState.Stopped,
        };
        host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return HostedChannelRuntimeControlResult.Success("Bot stopping.");
    }

    private HostedChannelRuntimeControlResult? CooldownMessage(BotHost host)
    {
        if (host.BotRuntimeStateChangedAtUtc is not { } changedAt)
            return null;

        if (RuntimeChangeCooldown == TimeSpan.Zero)
            return null;

        var nextAllowedAt = changedAt.Add(RuntimeChangeCooldown);
        return nextAllowedAt > DateTime.UtcNow
            ? HostedChannelRuntimeControlResult.Failure(
                $"Wait until {nextAllowedAt.ToLocalTime():HH:mm:ss} before changing bot state again.",
                nextAllowedAt
            )
            : null;
    }
}
