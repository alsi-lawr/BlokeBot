using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    private async Task<string?> MissingPrerequisiteAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        switch (configuration)
        {
            case SendChatActionConfiguration:
                return await ChatPrerequisiteAsync(db, context.Channel.Login, cancellationToken);
            case SendShoutoutActionConfiguration:
                var host = await db
                    .Hosts.AsNoTracking()
                    .Where(row => row.Id == hostId.Value)
                    .Select(row => new { row.Login, row.TwitchUserId })
                    .SingleOrDefaultAsync(cancellationToken);
                return host is null || string.IsNullOrWhiteSpace(host.TwitchUserId)
                        ? "shoutout-not-ready"
                    : await MissingCustomBotGrantAsync(
                        db,
                        host.Login,
                        ShoutoutService.RequiredScopes,
                        cancellationToken
                    )
                        ? "shoutout-not-ready"
                    : null;
            case FulfilRedemptionActionConfiguration or CancelRedemptionActionConfiguration:
                return await AutomationNativeActionPreparation
                    .Redemption(context)
                    .Match(
                        async prepared =>
                            await db
                                .TwitchCustomRewards.AsNoTracking()
                                .AnyAsync(
                                    row =>
                                        row.HostId == hostId.Value
                                        && row.ProviderRewardId == prepared.Payload.RewardId
                                        && row.IsManageable,
                                    cancellationToken
                                )
                                ? await BroadcasterPrerequisiteAsync(db, hostId, cancellationToken)
                                : "reward-not-manageable",
                        failed => Task.FromResult<string?>(failed.Code)
                    );
            case StartPollActionConfiguration
            or EndPollActionConfiguration
            or CreateClipActionConfiguration
            or CreateMarkerActionConfiguration
            or StartPredictionActionConfiguration
            or LockPredictionActionConfiguration
            or CancelPredictionActionConfiguration
            or ResolvePredictionActionConfiguration:
                return await BroadcasterPrerequisiteAsync(db, hostId, cancellationToken);
            default:
                return null;
        }
    }

    private static async Task<string?> BroadcasterPrerequisiteAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var hostSubject = await db
            .Hosts.AsNoTracking()
            .Where(row => row.Id == hostId.Value)
            .Select(row => row.TwitchUserId)
            .SingleOrDefaultAsync(cancellationToken);
        var grant = await db
            .HostBroadcasterAuthorizations.AsNoTracking()
            .Where(row => row.HostId == hostId.Value)
            .Select(row => new
            {
                HasPayload = row.ProtectedTokenPayload != null
                    && row.ProtectedTokenPayload.Length > 0,
                row.TwitchUserId,
                row.AuthorizedScopes,
            })
            .SingleOrDefaultAsync(cancellationToken);
        return
            grant is null
            || !grant.HasPayload
            || (
                !string.IsNullOrWhiteSpace(hostSubject)
                && !string.IsNullOrWhiteSpace(grant.TwitchUserId)
                && !string.Equals(hostSubject, grant.TwitchUserId, StringComparison.Ordinal)
            )
            || Missing(grant.AuthorizedScopes, HostBroadcasterAuthorizationService.MilestoneScopes)
            ? "broadcaster-authorization-missing"
            : null;
    }

    private async Task<string?> ChatPrerequisiteAsync(
        BlokeBotDbContext db,
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        if (
            await MissingCustomBotGrantAsync(
                db,
                channelLogin,
                settings.Identity.Scopes,
                cancellationToken
            )
        )
        {
            return "chat-rejected";
        }
        var normalized = Login.Normalize(channelLogin);
        var channel = await db
            .Hosts.AsNoTracking()
            .Where(row => row.Login == normalized)
            .Select(row => new { row.ChannelBotAuthorizedAtUtc, row.ChannelBotAuthorizedScopes })
            .SingleOrDefaultAsync(cancellationToken);
        return
            channel is not null
            && (
                channel.ChannelBotAuthorizedAtUtc is null
                || Missing(channel.ChannelBotAuthorizedScopes, channelRequiredScopes)
            )
            ? "chat-rejected"
            : null;
    }

    private static async Task<bool> MissingCustomBotGrantAsync(
        BlokeBotDbContext db,
        string channelLogin,
        IEnumerable<string> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        var normalized = Login.Normalize(channelLogin);
        var custom = await (
            from host in db.Hosts.AsNoTracking()
            join account in db.HostBotAccountSettings.AsNoTracking()
                on host.Id equals account.HostId
            where host.Login == normalized && account.OverrideEnabled
            select new
            {
                HasPayload = account.ProtectedTokenPayload != null
                    && account.ProtectedTokenPayload.Length > 0,
                account.AuthorizedScopes,
            }
        ).SingleOrDefaultAsync(cancellationToken);
        return custom is not null
            && (!custom.HasPayload || Missing(custom.AuthorizedScopes, requiredScopes));
    }

    private static bool Missing(string? granted, IEnumerable<string> required) =>
        ScopeSet
            .Missing(
                (granted ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries),
                required
            )
            .Length > 0;
}
