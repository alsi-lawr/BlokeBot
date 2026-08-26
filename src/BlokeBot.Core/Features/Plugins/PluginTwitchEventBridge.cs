using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginTwitchEventBridge(
    IPluginHostContextResolver hosts,
    IPluginDispatchSnapshotProvider dispatch,
    IPluginDispatchInvoker invoker,
    IPluginAutomationSourceAdmission? automationSources = null
) : IPluginTwitchEventObserver
{
    public Task StreamOnlineAsync(
        EventSubStreamOnlineEvent streamOnline,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.StreamOnline,
            streamOnline.BroadcasterUserLogin,
            streamOnline.MessageId,
            streamOnline.MessageTimestamp,
            null,
            new(streamOnline.StreamId, true),
            cancellation
        );

    public Task StreamOfflineAsync(
        EventSubStreamOfflineEvent streamOffline,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.StreamOffline,
            streamOffline.BroadcasterUserLogin,
            streamOffline.MessageId,
            streamOffline.MessageTimestamp,
            null,
            new(null, false),
            cancellation
        );

    public Task ChannelUpdatedAsync(
        EventSubChannelUpdateEvent channelUpdate,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.ChannelUpdated,
            channelUpdate.BroadcasterUserLogin,
            channelUpdate.MessageId,
            channelUpdate.MessageTimestamp,
            null,
            null,
            cancellation
        );

    public Task FollowReceivedAsync(EventSubFollowEvent follow, CancellationToken cancellation) =>
        DispatchAsync(
            PluginTwitchEventKind.FollowReceived,
            follow.BroadcasterUserLogin,
            follow.MessageId,
            follow.MessageTimestamp,
            Actor(follow.UserLogin, follow.UserName, follow.UserId),
            null,
            cancellation
        );

    public Task SubscriptionReceivedAsync(
        EventSubSubscriptionEvent subscription,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.SubscriptionReceived,
            subscription.BroadcasterUserLogin,
            subscription.MessageId,
            subscription.MessageTimestamp,
            Actor(subscription.UserLogin, subscription.UserName, subscription.UserId),
            null,
            cancellation
        );

    public Task SubscriptionGiftReceivedAsync(
        EventSubSubscriptionGiftEvent gift,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.SubscriptionGiftReceived,
            gift.BroadcasterUserLogin,
            gift.MessageId,
            gift.MessageTimestamp,
            Actor(gift.UserLogin, gift.UserName, gift.UserId),
            null,
            cancellation
        );

    public Task CheerReceivedAsync(EventSubCheerEvent cheer, CancellationToken cancellation) =>
        DispatchAsync(
            PluginTwitchEventKind.CheerReceived,
            cheer.BroadcasterUserLogin,
            cheer.MessageId,
            cheer.MessageTimestamp,
            Actor(cheer.UserLogin, cheer.UserName, cheer.UserId),
            null,
            cancellation
        );

    public Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.IncomingRaidReceived,
            incomingRaid.ToBroadcasterUserLogin,
            incomingRaid.MessageId,
            incomingRaid.MessageTimestamp,
            Actor(
                incomingRaid.FromBroadcasterUserLogin,
                incomingRaid.FromBroadcasterUserName,
                incomingRaid.FromBroadcasterUserId
            ),
            null,
            cancellation
        );

    public Task HypeTrainChangedAsync(
        EventSubHypeTrainEvent hypeTrain,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.HypeTrainChanged,
            hypeTrain.BroadcasterUserLogin,
            hypeTrain.MessageId,
            hypeTrain.MessageTimestamp,
            null,
            null,
            cancellation
        );

    public Task ChatNotificationReceivedAsync(
        EventSubChatNotificationEvent notification,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.ChatNotificationReceived,
            notification.BroadcasterUserLogin,
            notification.MessageId,
            notification.MessageTimestamp,
            Actor(
                notification.ChatterUserLogin,
                notification.ChatterUserName,
                notification.ChatterUserId
            ),
            null,
            cancellation
        );

    public Task RewardRedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.RewardRedemptionReceived,
            redemption.BroadcasterUserLogin,
            redemption.MessageId,
            redemption.RedeemedAt,
            Actor(redemption.UserLogin, redemption.UserName, redemption.UserId),
            null,
            cancellation
        );

    public Task ShoutoutOccurredAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.ShoutoutOccurred,
            shoutout.BroadcasterUserLogin,
            shoutout.MessageId,
            shoutout.StartedAt,
            null,
            null,
            cancellation
        );

    public Task PollChangedAsync(EventSubPollEvent poll, CancellationToken cancellation) =>
        DispatchAsync(
            PluginTwitchEventKind.PollChanged,
            poll.BroadcasterUserLogin,
            poll.MessageId,
            poll.StartedAt,
            null,
            null,
            cancellation
        );

    public Task PredictionChangedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellation
    ) =>
        DispatchAsync(
            PluginTwitchEventKind.PredictionChanged,
            prediction.BroadcasterUserLogin,
            prediction.MessageId,
            prediction.CreatedAt,
            null,
            null,
            cancellation
        );

    private async Task DispatchAsync(
        PluginTwitchEventKind kind,
        string channelLogin,
        string eventId,
        DateTimeOffset occurredAtUtc,
        PluginActorContext? actor,
        PluginStreamContext? stream,
        CancellationToken cancellationToken
    )
    {
        var host = await hosts.FindAsync(channelLogin, cancellationToken);
        if (host is null)
        {
            return;
        }
        var endpoints = dispatch
            .Current.Events.Where(endpoint =>
                endpoint.State.Key.HostId == host.Id
                && endpoint.Descriptor.Source is PluginEventSource.Twitch source
                && source.Kind == kind
            )
            .ToArray();
        foreach (var endpoint in endpoints)
        {
            var context = new PluginInvocationContext.Channel(
                endpoint.Declaration.Installation,
                host.Id,
                actor,
                stream,
                Event: new(endpoint.Descriptor.Id, EventName(kind), eventId, occurredAtUtc)
            );
            var outcome = await invoker.InvokeEventAsync(
                endpoint,
                context,
                new PluginValue.Map([
                    new("event_id", new PluginValue.String(eventId)),
                    new("source", new PluginValue.String(EventName(kind))),
                    new(
                        "occurred_at",
                        new PluginValue.String(occurredAtUtc.ToUniversalTime().ToString("O"))
                    ),
                ]),
                cancellationToken
            );
            if (
                outcome is PluginDispatchInvocationOutcome.Returned returned
                && !returned.AutomationSources.IsDefaultOrEmpty
                && automationSources is not null
            )
            {
                await automationSources.AdmitAsync(
                    endpoint,
                    context,
                    returned.AutomationSources,
                    cancellationToken
                );
            }
        }
    }

    private static PluginActorContext? Actor(string? login, string? name, string? userId) =>
        string.IsNullOrWhiteSpace(login)
            ? null
            : new(
                login,
                string.IsNullOrWhiteSpace(name) ? login : name,
                userId,
                false,
                false,
                false
            );

    private static string EventName(PluginTwitchEventKind kind) =>
        kind.ToString().ToLowerInvariant();
}
