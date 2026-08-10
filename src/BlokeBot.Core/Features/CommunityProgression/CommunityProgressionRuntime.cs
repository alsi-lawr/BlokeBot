using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CommunityProgression;

internal sealed class CommunityProgressionRuntime(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CommunityProgressionService progression,
    TimeProvider clock,
    ILogger<CommunityProgressionRuntime> log
) : ITwitchEventAutomationObserver, IChatMessageObserver, IEventSubRequirementSource
{
    public async ValueTask MessageReceivedAsync(
        ChatMessage message,
        CancellationToken cancellationToken
    )
    {
        if (
            !message.Tags.TryGetValue("id", out var messageId)
            || !message.Tags.TryGetValue("user-id", out var userId)
        )
        {
            return;
        }
        await HandleAsync(
            message.Channel,
            new CommunitySourceEvent.ChatMessage(
                messageId,
                new(userId, message.Login, message.Login),
                MessageOccurredAtUtc(message)
            ),
            cancellationToken
        );
    }

    public Task FollowReceivedAsync(EventSubFollowEvent follow, CancellationToken cancellation) =>
        HandleAsync(
            follow.BroadcasterUserId,
            follow.BroadcasterUserLogin,
            new CommunitySourceEvent.Follow(
                follow.MessageId,
                Viewer(follow.UserId, follow.UserLogin, follow.UserName),
                follow.FollowedAt
            ),
            cancellation
        );

    public Task SubscriptionReceivedAsync(
        EventSubSubscriptionEvent subscription,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            subscription.BroadcasterUserId,
            subscription.BroadcasterUserLogin,
            new CommunitySourceEvent.Subscription(
                subscription.MessageId,
                Viewer(subscription.UserId, subscription.UserLogin, subscription.UserName),
                subscription.Tier,
                subscription.MessageTimestamp
            ),
            cancellation
        );

    public Task CheerReceivedAsync(EventSubCheerEvent cheer, CancellationToken cancellation) =>
        HandleAsync(
            cheer.BroadcasterUserId,
            cheer.BroadcasterUserLogin,
            new CommunitySourceEvent.Cheer(
                cheer.MessageId,
                cheer.IsAnonymous || cheer.UserId is null || cheer.UserLogin is null
                    ? null
                    : Viewer(cheer.UserId, cheer.UserLogin, cheer.UserName ?? cheer.UserLogin),
                cheer.Bits,
                cheer.MessageTimestamp
            ),
            cancellation
        );

    public Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            incomingRaid.ToBroadcasterUserId,
            incomingRaid.ToBroadcasterUserLogin,
            new CommunitySourceEvent.IncomingRaid(
                incomingRaid.MessageId,
                Viewer(
                    incomingRaid.FromBroadcasterUserId,
                    incomingRaid.FromBroadcasterUserLogin,
                    incomingRaid.FromBroadcasterUserName
                ),
                incomingRaid.ViewerCount,
                incomingRaid.MessageTimestamp
            ),
            cancellation
        );

    public Task RewardRedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellation
    ) =>
        redemption.IsNewRedemption
            ? HandleAsync(
                redemption.BroadcasterUserId,
                redemption.BroadcasterUserLogin,
                new CommunitySourceEvent.RewardRedemption(
                    redemption.MessageId,
                    Viewer(redemption.UserId, redemption.UserLogin, redemption.UserName),
                    redemption.RewardId,
                    redemption.RedeemedAt
                ),
                cancellation
            )
            : Task.CompletedTask;

    public async ValueTask<bool> RequiresAsync(
        string channel,
        AutomationEventSubRequirement requirement,
        CancellationToken cancellation
    )
    {
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Login == login, cancellation);
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.CommunityProgression))
        {
            return false;
        }
        var rules = await (
            from definition in db.CommunityDefinitions.AsNoTracking()
            join season in db.CommunitySeasons.AsNoTracking()
                on definition.SeasonId equals season.Id
            where definition.HostId == host.Id && season.Status == CommunitySeasonStatus.Open
            select definition.EventRule
        ).ToListAsync(cancellation);
        return rules.Any(rule => Requirement(rule) == requirement);
    }

    public Task StreamOnlineAsync(
        EventSubStreamOnlineEvent streamOnline,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task StreamOfflineAsync(
        EventSubStreamOfflineEvent streamOffline,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task ChannelUpdatedAsync(
        EventSubChannelUpdateEvent channelUpdate,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task SubscriptionGiftReceivedAsync(
        EventSubSubscriptionGiftEvent gift,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task HypeTrainChangedAsync(
        EventSubHypeTrainEvent hypeTrain,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task ChatNotificationReceivedAsync(
        EventSubChatNotificationEvent notification,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task ShoutoutOccurredAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task PollChangedAsync(EventSubPollEvent poll, CancellationToken cancellation) =>
        Task.CompletedTask;

    public Task PredictionChangedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    private async Task HandleAsync(
        string broadcasterUserId,
        string broadcasterLogin,
        CommunitySourceEvent sourceEvent,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var hostId = await ResolveHostIdAsync(
                broadcasterUserId,
                broadcasterLogin,
                cancellationToken
            );
            if (hostId is not null)
            {
                _ = await progression.ProcessEventAsync(
                    hostId.Value,
                    sourceEvent,
                    cancellationToken
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogError(
                "Community progression source {SourceKind} failed with {FailureType}.",
                sourceEvent.Kind,
                exception.GetType().Name
            );
        }
    }

    private async Task HandleAsync(
        string broadcasterLogin,
        CommunitySourceEvent sourceEvent,
        CancellationToken cancellationToken
    ) => await HandleAsync(string.Empty, broadcasterLogin, sourceEvent, cancellationToken);

    private async Task<int?> ResolveHostIdAsync(
        string broadcasterUserId,
        string broadcasterLogin,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            return await db
                .Hosts.AsNoTracking()
                .Where(value => value.TwitchUserId == broadcasterUserId)
                .Select(value => (int?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }
        var login = CommunityInput.NormalizeLogin(broadcasterLogin);
        return await db
            .Hosts.AsNoTracking()
            .Where(value => value.Login == login)
            .Select(value => (int?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static CommunityViewer Viewer(string userId, string login, string displayName) =>
        new(userId, login, string.IsNullOrWhiteSpace(displayName) ? login : displayName);

    private DateTimeOffset MessageOccurredAtUtc(ChatMessage message) =>
        message.Tags.TryGetValue("tmi-sent-ts", out var timestamp)
        && long.TryParse(timestamp, out var unixMilliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
            : clock.GetUtcNow();

    private static AutomationEventSubRequirement? Requirement(CommunityEventRuleKind rule) =>
        rule switch
        {
            CommunityEventRuleKind.Follow => AutomationEventSubRequirement.Follows,
            CommunityEventRuleKind.Subscription => AutomationEventSubRequirement.Subscriptions,
            CommunityEventRuleKind.Cheer => AutomationEventSubRequirement.Cheers,
            CommunityEventRuleKind.IncomingRaid => AutomationEventSubRequirement.IncomingRaids,
            CommunityEventRuleKind.RewardRedemption => AutomationEventSubRequirement.Redemptions,
            _ => null,
        };
}

internal sealed class CommunityProgressionScheduleWorker(
    CommunityProgressionService progression,
    TimeProvider clock
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await progression.RollOverCurrentPeriodsAsync(CommunityRolloverKind.Restart, stoppingToken);
        await progression.ReconcileCompletedBountyEventsAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await progression.RollOverCurrentPeriodsAsync(
                CommunityRolloverKind.Timer,
                stoppingToken
            );
            await progression.ReconcileCompletedBountyEventsAsync(stoppingToken);
        }
    }
}

internal sealed class CommunityProgressionFeatureObserver(
    CommunityProgressionService progression,
    IEventSubChannelReconciliationTrigger? eventSub = null
) : IHostFeatureChangeObserver
{
    public async ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (feature != HostFeatureFlags.CommunityProgression)
        {
            return;
        }
        if (eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }
        if (enabled)
        {
            await progression.RollOverCurrentPeriodsAsync(
                CommunityRolloverKind.Restart,
                cancellationToken
            );
        }
    }
}
