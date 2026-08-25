namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelOperations
{
    public ValueTask<EventSubSubscriptionSetupOutcome> CreateExactSubscriptionAsync(
        string channel,
        BotAccount account,
        EventSubExactSubscription subscription,
        CancellationToken cancellationToken
    ) =>
        CreateBroadcasterOperationSubscriptionsAsync(
            channel,
            EventSubAuthorizationContext.ConfiguredBotAuthority,
            account,
            [(subscription.Type, subscription.Version)],
            cancellationToken
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotRaidSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    )
    {
        var broadcasterId = await identities.ResolveBroadcasterIdAsync(
            channel,
            account.AccessToken,
            ct
        );
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return new EventSubSubscriptionSetupOutcome.MissingChannel();
        }

        var id = await CreateAsync(
            "channel.raid",
            new Dictionary<string, string> { ["to_broadcaster_user_id"] = broadcasterId },
            ct
        );
        return new EventSubSubscriptionSetupOutcome.Created(
            CreateActive(channel, authorization, account, [id])
        );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotOutgoingRaidSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    )
    {
        var broadcasterId = await identities.ResolveBroadcasterIdAsync(
            channel,
            account.AccessToken,
            ct
        );
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return new EventSubSubscriptionSetupOutcome.MissingChannel();
        }

        var id = await CreateAsync(
            "channel.raid",
            new Dictionary<string, string> { ["from_broadcaster_user_id"] = broadcasterId },
            ct
        );
        return new EventSubSubscriptionSetupOutcome.Created(
            CreateActive(channel, authorization, account, [id])
        );
    }

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBotConditionSubscriptionsAsync(
            channel,
            authorization,
            account,
            [("channel.chat.message", "1", "user_id")],
            ct
        );

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotOperationSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBotConditionSubscriptionsAsync(
            channel,
            authorization,
            account,
            [
                ("channel.shoutout.create", "1", "moderator_user_id"),
                ("channel.shoutout.receive", "1", "moderator_user_id"),
            ],
            ct
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateAutomationStreamSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        await CreateBroadcasterOperationSubscriptionsAsync(
            channel,
            authorization,
            account,
            [("stream.online", "1"), ("stream.offline", "1")],
            ct
        );

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateAutomationBotConditionSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<(string Type, string Version, string BotConditionKey)> subscriptionTypes,
        CancellationToken ct
    ) =>
        CreateBotConditionSubscriptionsAsync(
            channel,
            authorization,
            account,
            subscriptionTypes,
            ct
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateBotConditionSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<(string Type, string Version, string BotConditionKey)> subscriptionTypes,
        CancellationToken ct
    )
    {
        var resolution = await identities.ResolveAsync(
            channel,
            account.Login,
            account.AccessToken,
            ct
        );
        return await resolution.Match(
            resolved =>
                CreateSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    subscriptionTypes,
                    (subscription, token) =>
                        CreateAsync(
                            subscription.Type,
                            subscription.Version,
                            new Dictionary<string, string>
                            {
                                ["broadcaster_user_id"] = resolved.BroadcasterId,
                                [subscription.BotConditionKey] = resolved.BotUserId,
                            },
                            token
                        ),
                    ct
                ),
            static _ =>
                ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                    new EventSubSubscriptionSetupOutcome.MissingChannel()
                ),
            static _ =>
                ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                    new EventSubSubscriptionSetupOutcome.MissingBot()
                )
        );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateBroadcasterOperationSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<(string Type, string Version)> subscriptionTypes,
        CancellationToken ct
    )
    {
        var broadcasterId = await identities.ResolveBroadcasterIdAsync(
            channel,
            account.AccessToken,
            ct
        );
        return string.IsNullOrWhiteSpace(broadcasterId)
            ? new EventSubSubscriptionSetupOutcome.MissingChannel()
            : await CreateSubscriptionsAsync(
                channel,
                authorization,
                account,
                subscriptionTypes,
                (subscription, token) =>
                    CreateAsync(
                        subscription.Type,
                        subscription.Version,
                        new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterId },
                        token
                    ),
                ct
            );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionsAsync<TSubscription>(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<TSubscription> subscriptionTypes,
        Func<TSubscription, CancellationToken, Task<string>> createAsync,
        CancellationToken ct
    )
    {
        var ids = new List<string>();
        try
        {
            foreach (var subscription in subscriptionTypes)
            {
                ids.Add(await createAsync(subscription, ct));
            }
            return new EventSubSubscriptionSetupOutcome.Created(
                CreateActive(channel, authorization, account, ids)
            );
        }
        catch (Exception exception) when (ids.Count > 0)
        {
            return new EventSubSubscriptionSetupOutcome.PartiallyCreated(
                CreateActive(channel, authorization, account, ids),
                exception
            );
        }
    }

    private Task<string> CreateAsync(
        string type,
        IReadOnlyDictionary<string, string> condition,
        CancellationToken cancellationToken
    ) => CreateAsync(type, "1", condition, cancellationToken);

    private Task<string> CreateAsync(
        string type,
        string version,
        IReadOnlyDictionary<string, string> condition,
        CancellationToken cancellationToken
    ) =>
        subscriptions.CreateAsync(
            settings.Identity.ClientId,
            new EventSubSubscriptionRequest(type, version, condition),
            cancellationToken
        );

    private static ActiveEventSubSubscription CreateActive(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<string> ids
    ) =>
        new()
        {
            Channel = channel,
            SubscriptionId = ids[0],
            AdditionalSubscriptionIds = ids.Skip(1).ToArray(),
            BotLogin = account.Login,
            Authorization = authorization,
            Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
        };
}
