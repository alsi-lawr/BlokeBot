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
    HostBotAccountAuthorizationService botAccounts,
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
            return HostedChannelRuntimeControlResult.Failure("Channel setup was not found.");

        if (
            !channelBotAuthorization.IsCurrent(
                host.ChannelBotAuthorizedAtUtc,
                host.ChannelBotAuthorizedScopes
            )
        )
        {
            return HostedChannelRuntimeControlResult.Failure(
                "Connect the bot to Twitch chat before starting it."
            );
        }

        var botAccountStatus = await botAccounts.GetStatusAsync(host.Id, ct);
        if (
            botAccountStatus.State
            is not BotAccountAuthorizationState.Disabled
                and not BotAccountAuthorizationState.Ready
        )
        {
            return HostedChannelRuntimeControlResult.Failure(
                "Connect the custom bot account before starting it, or turn custom bot off."
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
            return HostedChannelRuntimeControlResult.Failure("Channel setup was not found.");

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
                $"Wait until {nextAllowedAt.ToLocalTime():HH:mm:ss} before starting or stopping the bot again.",
                nextAllowedAt
            )
            : null;
    }
}
