using BlokeBot.Core;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public sealed class ShoutoutService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBotAccountTokenStatusProvider accounts,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    NativeTwitchFeatureGate nativeTwitch
) : IShoutoutEventObserver, IAutomaticRaidNativeShoutoutOperation, IShoutoutDashboardOperations
{
    internal const string UnauthorizedAuthorityMessage =
        "Twitch rejected the configured bot's shoutout authority.";

    private static readonly string[] _requiredScopes =
    [
        Scopes.UserReadModeratedChannels,
        Scopes.ModeratorReadShoutouts,
        Scopes.ModeratorManageShoutouts,
    ];

    public async Task<ShoutoutDashboardState> LoadAsync(
        int hostId,
        string? targetLogin,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Shoutouts, ct))
        {
            return new(null, new ShoutoutTargetCooldownReadiness.Unknown(), []);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cooldown = await db
            .ShoutoutCooldowns.AsNoTracking()
            .Where(x => x.HostId == hostId && x.TargetTwitchUserId == null)
            .Select(x => x.GlobalEligibleAtUtc)
            .SingleOrDefaultAsync(ct);
        var normalizedTarget = Login.Normalize(targetLogin);
        ShoutoutTargetCooldownReadiness targetCooldown =
            string.IsNullOrWhiteSpace(normalizedTarget)
                ? new ShoutoutTargetCooldownReadiness.Unknown()
            : await db
                .ShoutoutCooldowns.AsNoTracking()
                .Where(x => x.HostId == hostId && x.TargetLogin == normalizedTarget)
                .Select(x => x.TargetEligibleAtUtc)
                .SingleOrDefaultAsync(ct)
                is { } eligibleAt
                ? new ShoutoutTargetCooldownReadiness.EligibleAt(eligibleAt)
            : new ShoutoutTargetCooldownReadiness.Unknown();
        var history = await db
            .ShoutoutHistory.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(100)
            .Select(x => new ShoutoutHistoryView(
                x.Direction == ShoutoutHistoryDirection.Sent
                    ? ShoutoutDirection.Sent
                    : ShoutoutDirection.Received,
                x.SourceLogin,
                x.TargetLogin,
                x.ViewerCount,
                x.OccurredAtUtc,
                x.CooldownEndsAtUtc,
                x.TargetCooldownEndsAtUtc
            ))
            .ToArrayAsync(ct);
        return new(cooldown, targetCooldown, history);
    }

    public async Task<ShoutoutOperationOutcome> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Shoutouts, ct))
        {
            return new ShoutoutOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var normalizedTarget = Login.Normalize(targetLogin);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return new ShoutoutOperationOutcome.TargetNotFound(targetLogin);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var broadcasterId = host?.TwitchUserId;
        if (host is null || string.IsNullOrWhiteSpace(broadcasterId))
        {
            return new ShoutoutOperationOutcome.NotReady(
                "Select a connected Twitch channel first."
            );
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Shoutouts))
        {
            return new ShoutoutOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var account = await accounts.GetActiveTokenStatusAsync(host.Login, _requiredScopes, ct);
        return await account.Status.Match(
            _ =>
                Task.FromResult<ShoutoutOperationOutcome>(
                    new ShoutoutOperationOutcome.NotReady(
                        "BlokeBot could not check the bot account."
                    )
                ),
            _ =>
                Task.FromResult<ShoutoutOperationOutcome>(
                    new ShoutoutOperationOutcome.NotReady(
                        "Connect the bot account with shoutout permissions."
                    )
                ),
            _ =>
                Task.FromResult<ShoutoutOperationOutcome>(
                    new ShoutoutOperationOutcome.NotReady(
                        "Reconnect the bot account with shoutout permissions."
                    )
                ),
            missing =>
                SendAuthorizedAsync(
                    db,
                    host,
                    broadcasterId,
                    normalizedTarget,
                    missing.AccessToken,
                    missing.Validation.UserId,
                    missing.GrantedScopes,
                    ct
                ),
            ready =>
                SendAuthorizedAsync(
                    db,
                    host,
                    broadcasterId,
                    normalizedTarget,
                    ready.AccessToken,
                    ready.Validation.UserId,
                    ready.GrantedScopes,
                    ct
                )
        );
    }

    public async Task ShoutoutReceivedAsync(EventSubShoutoutEvent shoutout, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(
            x =>
                x.TwitchUserId == shoutout.BroadcasterUserId
                || x.Login == Login.Normalize(shoutout.BroadcasterUserLogin),
            ct
        );
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.Shoutouts))
        {
            return;
        }

        if (
            !string.IsNullOrWhiteSpace(shoutout.MessageId)
            && await db.ShoutoutHistory.AnyAsync(
                x => x.HostId == host.Id && x.ProviderMessageId == shoutout.MessageId,
                ct
            )
        )
        {
            return;
        }
        var direction =
            shoutout.Direction == EventSubShoutoutDirection.Sent
                ? ShoutoutHistoryDirection.Sent
                : ShoutoutHistoryDirection.Received;
        db.ShoutoutHistory.Add(
            new ShoutoutHistoryEntry
            {
                HostId = host.Id,
                Direction = direction,
                ProviderMessageId = string.IsNullOrWhiteSpace(shoutout.MessageId)
                    ? null
                    : shoutout.MessageId,
                SourceTwitchUserId = shoutout.FromBroadcasterUserId,
                SourceLogin = Login.Normalize(shoutout.FromBroadcasterUserLogin),
                TargetTwitchUserId = shoutout.ToBroadcasterUserId,
                TargetLogin = Login.Normalize(shoutout.ToBroadcasterUserLogin),
                ViewerCount = shoutout.ViewerCount,
                OccurredAtUtc = shoutout.StartedAt.UtcDateTime,
                CooldownEndsAtUtc = shoutout.CooldownEndsAt?.UtcDateTime,
                TargetCooldownEndsAtUtc = shoutout.TargetCooldownEndsAt?.UtcDateTime,
            }
        );
        if (direction == ShoutoutHistoryDirection.Sent)
        {
            await StoreCooldownAsync(
                db,
                host.Id,
                null,
                null,
                shoutout.CooldownEndsAt?.UtcDateTime,
                ct
            );
            await StoreCooldownAsync(
                db,
                host.Id,
                shoutout.ToBroadcasterUserId,
                Login.Normalize(shoutout.ToBroadcasterUserLogin),
                shoutout.TargetCooldownEndsAt?.UtcDateTime,
                ct
            );
        }
        await TrimHistoryAsync(db, host.Id, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    private async Task<ShoutoutOperationOutcome> SendAuthorizedAsync(
        BlokeBotDbContext db,
        BotHost host,
        string broadcasterId,
        string targetLogin,
        string accessToken,
        string botId,
        IReadOnlyList<string> grantedScopes,
        CancellationToken ct
    )
    {
        if (ScopeSet.Missing(grantedScopes, _requiredScopes).Length > 0)
        {
            return new ShoutoutOperationOutcome.NotReady(
                "Reconnect the bot account with both Twitch shoutout permissions."
            );
        }
        var context = new HelixRequestContext(settings.Identity.ClientId, accessToken);
        var target = await helix.GetShoutoutTargetAsync(context, targetLogin, ct);
        if (target is null)
        {
            return new ShoutoutOperationOutcome.TargetNotFound(targetLogin);
        }
        if (target.Id == broadcasterId)
        {
            return new ShoutoutOperationOutcome.SelfTarget();
        }
        var stream = await helix.GetStreamAsync(context, target.Login, ct);
        if (stream is null || stream.ViewerCount == 0)
        {
            return new ShoutoutOperationOutcome.TargetOffline(target.Login);
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cooldown = await db
            .ShoutoutCooldowns.AsNoTracking()
            .Where(x =>
                x.HostId == host.Id
                && (x.TargetTwitchUserId == null || x.TargetTwitchUserId == target.Id)
            )
            .OrderByDescending(x => x.TargetTwitchUserId != null)
            .Select(x => x.TargetEligibleAtUtc ?? x.GlobalEligibleAtUtc)
            .FirstOrDefaultAsync(ct);
        if (cooldown is { } eligibleAt && eligibleAt > now)
        {
            return new ShoutoutOperationOutcome.CooldownActive(eligibleAt);
        }
        var authority =
            botId == broadcasterId || await IsModeratorAsync(context, botId, broadcasterId, ct);
        if (!authority)
        {
            return new ShoutoutOperationOutcome.NotReady(
                "The configured bot must be this channel's broadcaster or moderator."
            );
        }
        if (!await nativeTwitch.IsEnabledAsync(host.Id, HostFeatureFlags.Shoutouts, ct))
        {
            return new ShoutoutOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var result = await helix.SendShoutoutAsync(context, broadcasterId, botId, target.Id, ct);
        return result switch
        {
            ShoutoutSendResult.Sent => new ShoutoutOperationOutcome.Sent(target.Login),
            ShoutoutSendResult.Cooldown => new ShoutoutOperationOutcome.CooldownUnknown(),
            ShoutoutSendResult.Unauthorized => new ShoutoutOperationOutcome.NotReady(
                UnauthorizedAuthorityMessage
            ),
            ShoutoutSendResult.InvalidTarget => new ShoutoutOperationOutcome.ProviderRejected(
                "Twitch rejected that shoutout target."
            ),
            _ => new ShoutoutOperationOutcome.ProviderRejected(
                "Twitch could not confirm the shoutout."
            ),
        };
    }

    private async Task<bool> IsModeratorAsync(
        HelixRequestContext context,
        string botId,
        string broadcasterId,
        CancellationToken ct
    )
    {
        return await helix.GetModeratedChannelStatusAsync(context, botId, broadcasterId, ct)
            is ModeratedChannelStatus.IsModerator;
    }

    private static async Task StoreCooldownAsync(
        BlokeBotDbContext db,
        int hostId,
        string? targetId,
        string? targetLogin,
        DateTime? eligibleAtUtc,
        CancellationToken ct
    )
    {
        var state = await db.ShoutoutCooldowns.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.TargetTwitchUserId == targetId,
            ct
        );
        if (state is null)
        {
            state = new ShoutoutCooldownState { HostId = hostId, TargetTwitchUserId = targetId };
            db.ShoutoutCooldowns.Add(state);
        }
        state.GlobalEligibleAtUtc = targetId is null ? eligibleAtUtc : null;
        state.TargetLogin = targetId is null ? null : targetLogin;
        state.TargetEligibleAtUtc = targetId is null ? null : eligibleAtUtc;
        state.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static async Task TrimHistoryAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var excess = await db
            .ShoutoutHistory.Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Skip(100)
            .ToArrayAsync(ct);
        db.ShoutoutHistory.RemoveRange(excess);
    }
}
