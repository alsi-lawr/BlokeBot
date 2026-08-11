using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Starts automation flows from Twitch EventSub deliveries. Every delivery resolves to exactly one
/// host, is blocked before any mutation while Automations is off, and is admitted through a durable
/// host/source/message-ID receipt with exactly <see cref="ReceiptAuthorityWindow"/> of
/// deduplication authority. Flow failures never affect EventSub acknowledgement, which the
/// transport completes before observers run.
/// </summary>
public sealed class TwitchEventAutomationRuntime(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationRuntimeService runtime,
    TimeProvider clock,
    ILogger<TwitchEventAutomationRuntime> log
) : ITwitchEventAutomationObserver, IAutomationEventSubRequirementSource
{
    internal static readonly TimeSpan ReceiptAuthorityWindow = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan ReceiptCleanupInterval = TimeSpan.FromMinutes(4);
    internal const int MaximumMessageIdLength = 128;

    public Task StreamOnlineAsync(
        EventSubStreamOnlineEvent streamOnline,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            streamOnline.BroadcasterUserId,
            streamOnline.BroadcasterUserLogin,
            streamOnline.MessageId,
            AutomationDefinitionIds.StreamOnlineSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.StreamOnline(host, streamOnline, receivedAtUtc),
            static configuration => configuration is StreamOnlineSourceConfiguration,
            cancellation
        );

    public Task StreamOfflineAsync(
        EventSubStreamOfflineEvent streamOffline,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            streamOffline.BroadcasterUserId,
            streamOffline.BroadcasterUserLogin,
            streamOffline.MessageId,
            AutomationDefinitionIds.StreamOfflineSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.StreamOffline(host, streamOffline, receivedAtUtc),
            static configuration => configuration is StreamOfflineSourceConfiguration,
            cancellation
        );

    public Task ChannelUpdatedAsync(
        EventSubChannelUpdateEvent channelUpdate,
        CancellationToken cancellation
    ) => Task.CompletedTask;

    public Task FollowReceivedAsync(EventSubFollowEvent follow, CancellationToken cancellation) =>
        HandleAsync(
            follow.BroadcasterUserId,
            follow.BroadcasterUserLogin,
            follow.MessageId,
            AutomationDefinitionIds.FollowSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.Follow(host, follow, receivedAtUtc),
            static configuration => configuration is FollowSourceConfiguration,
            cancellation
        );

    public Task SubscriptionReceivedAsync(
        EventSubSubscriptionEvent subscription,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            subscription.BroadcasterUserId,
            subscription.BroadcasterUserLogin,
            subscription.MessageId,
            AutomationDefinitionIds.SubscriptionSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.Subscription(host, subscription, receivedAtUtc),
            static configuration => configuration is SubscriptionSourceConfiguration,
            cancellation
        );

    public Task SubscriptionGiftReceivedAsync(
        EventSubSubscriptionGiftEvent gift,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            gift.BroadcasterUserId,
            gift.BroadcasterUserLogin,
            gift.MessageId,
            AutomationDefinitionIds.SubscriptionGiftSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.SubscriptionGift(host, gift, receivedAtUtc),
            configuration =>
                configuration is SubscriptionGiftSourceConfiguration required
                && gift.Total >= required.MinimumGiftCount,
            cancellation
        );

    public Task CheerReceivedAsync(EventSubCheerEvent cheer, CancellationToken cancellation) =>
        HandleAsync(
            cheer.BroadcasterUserId,
            cheer.BroadcasterUserLogin,
            cheer.MessageId,
            AutomationDefinitionIds.CheerSource,
            (host, receivedAtUtc) => TwitchEventAutomationContext.Cheer(host, cheer, receivedAtUtc),
            configuration =>
                configuration is CheerSourceConfiguration required
                && cheer.Bits >= required.MinimumBits,
            cancellation
        );

    public Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            incomingRaid.ToBroadcasterUserId,
            incomingRaid.ToBroadcasterUserLogin,
            incomingRaid.MessageId,
            AutomationDefinitionIds.IncomingRaidSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.IncomingRaid(host, incomingRaid, receivedAtUtc),
            configuration =>
                configuration is IncomingRaidSourceConfiguration required
                && incomingRaid.ViewerCount >= required.MinimumViewerCount,
            cancellation
        );

    public Task HypeTrainChangedAsync(
        EventSubHypeTrainEvent hypeTrain,
        CancellationToken cancellation
    )
    {
        var definitionId = hypeTrain.Stage switch
        {
            EventSubHypeTrainStage.Begin => AutomationDefinitionIds.HypeTrainBeginSource,
            EventSubHypeTrainStage.Progress => AutomationDefinitionIds.HypeTrainProgressSource,
            _ => AutomationDefinitionIds.HypeTrainEndSource,
        };
        return HandleAsync(
            hypeTrain.BroadcasterUserId,
            hypeTrain.BroadcasterUserLogin,
            hypeTrain.MessageId,
            definitionId,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.HypeTrain(
                    host,
                    definitionId,
                    hypeTrain,
                    receivedAtUtc
                ),
            static configuration => configuration is HypeTrainSourceConfiguration,
            cancellation
        );
    }

    public Task ChatNotificationReceivedAsync(
        EventSubChatNotificationEvent notification,
        CancellationToken cancellation
    ) =>
        HandleAsync(
            notification.BroadcasterUserId,
            notification.BroadcasterUserLogin,
            notification.MessageId,
            AutomationDefinitionIds.ChatNotificationSource,
            (host, receivedAtUtc) =>
                TwitchEventAutomationContext.ChatNotification(host, notification, receivedAtUtc),
            configuration =>
                configuration is ChatNotificationSourceConfiguration required
                && (
                    required.NoticeType == "any"
                    || string.Equals(
                        required.NoticeType,
                        notification.NoticeType,
                        StringComparison.Ordinal
                    )
                ),
            cancellation
        );

    public Task RewardRedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellation
    ) =>
        // Only channel.channel_points_custom_reward_redemption.add deliveries start flows; a
        // status update never re-triggers the redemption's automation.
        redemption.IsNewRedemption
            ? HandleAsync(
                redemption.BroadcasterUserId,
                redemption.BroadcasterUserLogin,
                redemption.MessageId,
                AutomationDefinitionIds.RewardRedemptionSource,
                (host, receivedAtUtc) =>
                    RedemptionAutomationContext.Create(host, redemption, receivedAtUtc),
                configuration =>
                    configuration is RewardRedemptionSourceConfiguration required
                    && (
                        required.RewardId is null
                        || string.Equals(
                            required.RewardId,
                            redemption.RewardId,
                            StringComparison.Ordinal
                        )
                    ),
                cancellation,
                HostFeatureFlags.Automations | HostFeatureFlags.RewardsAndRedemptions
            )
            : Task.CompletedTask;

    public Task ShoutoutOccurredAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellation
    )
    {
        var definitionId =
            shoutout.Direction == EventSubShoutoutDirection.Sent
                ? AutomationDefinitionIds.ShoutoutSentSource
                : AutomationDefinitionIds.ShoutoutReceivedSource;
        return HandleAsync(
            shoutout.BroadcasterUserId,
            shoutout.BroadcasterUserLogin,
            shoutout.MessageId,
            definitionId,
            (host, receivedAtUtc) =>
                NativeOperationAutomationContext.Shoutout(
                    host,
                    definitionId,
                    shoutout,
                    receivedAtUtc
                ),
            configuration =>
                shoutout.Direction == EventSubShoutoutDirection.Sent
                    ? configuration is ShoutoutSentSourceConfiguration
                    : configuration is ShoutoutReceivedSourceConfiguration,
            cancellation,
            HostFeatureFlags.Automations | HostFeatureFlags.Shoutouts
        );
    }

    public Task PollChangedAsync(EventSubPollEvent poll, CancellationToken cancellation)
    {
        var definitionId = poll.Stage switch
        {
            EventSubPollStage.Begin => AutomationDefinitionIds.PollStartedSource,
            EventSubPollStage.Progress => AutomationDefinitionIds.PollProgressedSource,
            _ => AutomationDefinitionIds.PollEndedSource,
        };
        return HandleAsync(
            poll.BroadcasterUserId,
            poll.BroadcasterUserLogin,
            poll.MessageId,
            definitionId,
            (host, receivedAtUtc) =>
                NativeOperationAutomationContext.Poll(host, definitionId, poll, receivedAtUtc),
            configuration =>
                poll.Stage switch
                {
                    EventSubPollStage.Begin => configuration is PollStartedSourceConfiguration,
                    EventSubPollStage.Progress => configuration
                        is PollProgressedSourceConfiguration,
                    _ => configuration is PollEndedSourceConfiguration,
                },
            cancellation,
            HostFeatureFlags.Automations | HostFeatureFlags.Polls
        );
    }

    public Task PredictionChangedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellation
    )
    {
        var definitionId = prediction.Stage switch
        {
            EventSubPredictionStage.Begin => AutomationDefinitionIds.PredictionStartedSource,
            EventSubPredictionStage.Progress => AutomationDefinitionIds.PredictionProgressedSource,
            EventSubPredictionStage.Lock => AutomationDefinitionIds.PredictionLockedSource,
            _ => AutomationDefinitionIds.PredictionEndedSource,
        };
        return HandleAsync(
            prediction.BroadcasterUserId,
            prediction.BroadcasterUserLogin,
            prediction.MessageId,
            definitionId,
            (host, receivedAtUtc) =>
                NativeOperationAutomationContext.Prediction(
                    host,
                    definitionId,
                    prediction,
                    receivedAtUtc
                ),
            configuration =>
                prediction.Stage switch
                {
                    EventSubPredictionStage.Begin => configuration
                        is PredictionStartedSourceConfiguration,
                    EventSubPredictionStage.Progress => configuration
                        is PredictionProgressedSourceConfiguration,
                    EventSubPredictionStage.Lock => configuration
                        is PredictionLockedSourceConfiguration,
                    _ => configuration is PredictionEndedSourceConfiguration,
                },
            cancellation,
            HostFeatureFlags.Automations | HostFeatureFlags.Predictions
        );
    }

    public async ValueTask<bool> RequiresAsync(
        string channel,
        AutomationEventSubRequirement requirement,
        CancellationToken cancellation
    )
    {
        var host = await ResolveHostByLoginAsync(channel, cancellation);
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return false;
        }

        var enabledSources = await runtime.EnabledSourceDefinitionIdsAsync(
            new(host.Id),
            cancellation
        );
        return TwitchEventAutomationSources
            .All.Where(source => source.Requirement == requirement)
            .Any(source => enabledSources.Contains(source.DefinitionId.Value));
    }

    /// <summary>
    /// Physically deletes receipts whose ten-minute authority has ended. Callers run this at least
    /// every five minutes; claims additionally clean expired rows before inserting.
    /// </summary>
    public async Task CleanupExpiredReceiptsAsync(CancellationToken cancellation)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var now = clock.GetUtcNow().UtcDateTime;
        _ = await db
            .AutomationEventReceipts.Where(receipt => receipt.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellation);
    }

    private async Task HandleAsync(
        string broadcasterUserId,
        string broadcasterUserLogin,
        string messageId,
        AutomationDefinitionId definitionId,
        Func<BotHost, DateTimeOffset, AutomationContext> createContext,
        Func<AutomationConfiguration, bool> matches,
        CancellationToken cancellation,
        HostFeatureFlags requiredFeatures = HostFeatureFlags.Automations
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > MaximumMessageIdLength)
            {
                return;
            }

            var host = await ResolveHostAsync(
                broadcasterUserId,
                broadcasterUserLogin,
                cancellation
            );
            if (host is null)
            {
                return;
            }

            // The feature-switch gate runs before any receipt or run row is written. Every source
            // requires Automations; native-operation deliveries additionally require their single
            // backing Native Twitch feature switch, such as Rewards & redemptions, Shoutouts,
            // Polls, or Predictions.
            if (!host.EnabledFeatures.Contains(requiredFeatures))
            {
                return;
            }

            var receivedAtUtc = clock.GetUtcNow();
            if (
                !await TryClaimReceiptAsync(
                    host.Id,
                    definitionId,
                    messageId,
                    receivedAtUtc,
                    cancellation
                )
            )
            {
                return;
            }

            _ = await runtime.DispatchTwitchEventAsync(
                createContext(host, receivedAtUtc),
                matches,
                cancellation
            );
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // EventSub acknowledgement already happened at the transport; a failed automation
            // dispatch must not affect other observers or deliveries.
            log.LogError(
                "Twitch event automation dispatch for {Source} failed with {FailureType}.",
                definitionId.Value,
                exception.GetType().Name
            );
        }
    }

    private async Task<bool> TryClaimReceiptAsync(
        int hostId,
        AutomationDefinitionId definitionId,
        string messageId,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellation
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        var now = receivedAtUtc.UtcDateTime;
        // A receipt at or past its expiry has no deduplication authority; remove dead rows before
        // claiming so a post-window redelivery is admitted as a new occurrence.
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM automation_event_receipts WHERE ExpiresAtUtc <= {now};",
            cancellation
        );
        var expiresAtUtc = now + ReceiptAuthorityWindow;
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO automation_event_receipts
                (HostId, SourceDefinitionId, ProviderMessageId, ClaimedAtUtc, ExpiresAtUtc)
            VALUES
                ({hostId}, {definitionId.Value}, {messageId}, {now}, {expiresAtUtc});
            """,
            cancellation
        );
        await transaction.CommitAsync(cancellation);
        return claimed == 1;
    }

    private async Task<BotHost?> ResolveHostAsync(
        string broadcasterUserId,
        string broadcasterUserLogin,
        CancellationToken cancellation
    )
    {
        if (!string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellation);
            return await db
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(host => host.TwitchUserId == broadcasterUserId, cancellation);
        }

        return await ResolveHostByLoginAsync(broadcasterUserLogin, cancellation);
    }

    private async Task<BotHost?> ResolveHostByLoginAsync(
        string channelLogin,
        CancellationToken cancellation
    )
    {
        var normalized = Login.Normalize(channelLogin);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        return await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(host => host.Login == normalized, cancellation);
    }
}

internal sealed class AutomationEventReceiptCleanupWorker(
    TwitchEventAutomationRuntime runtime,
    TimeProvider clock
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TwitchEventAutomationRuntime.ReceiptCleanupInterval,
            clock
        );
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await runtime.CleanupExpiredReceiptsAsync(stoppingToken);
        }
    }
}
