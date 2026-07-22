namespace BlokeBot.Twitch.Runtime;

internal sealed class HelixPublicChatPinProvider(
    AppAccessTokenProvider appTokens,
    IBotAccountProvider botAccounts,
    BotIdentity identity,
    ChatIdentityResolver identities,
    ChatPinClient pins
) : IPublicChatPinProvider
{
    public async ValueTask<PublicChatPinExecutionOutcome> ExecuteAsync(
        PublicChatPinWorkItem item,
        CancellationToken cancellationToken
    )
    {
        var botAccount = await botAccounts
            .GetBotAccount(item.Channel)
            .ExecuteAsync(cancellationToken);
        return await botAccount.Match(
            account => ExecuteForAccountAsync(item, account, cancellationToken),
            reason =>
                ValueTask.FromResult<PublicChatPinExecutionOutcome>(
                    new PublicChatPinExecutionOutcome.Terminal($"token:{reason}")
                )
        );
    }

    private async ValueTask<PublicChatPinExecutionOutcome> ExecuteForAccountAsync(
        PublicChatPinWorkItem item,
        BotAccount account,
        CancellationToken cancellationToken
    )
    {
        var resolved = await identities.ResolveAsync(
            item.Channel,
            account.Login,
            account.AccessToken,
            cancellationToken
        );
        return await resolved.Match(
            ids => ExecuteResolvedAsync(item, ids, cancellationToken),
            _ =>
                ValueTask.FromResult<PublicChatPinExecutionOutcome>(
                    new PublicChatPinExecutionOutcome.Terminal("missing-channel")
                ),
            _ =>
                ValueTask.FromResult<PublicChatPinExecutionOutcome>(
                    new PublicChatPinExecutionOutcome.Terminal("missing-bot")
                )
        );
    }

    private async ValueTask<PublicChatPinExecutionOutcome> ExecuteResolvedAsync(
        PublicChatPinWorkItem item,
        ChatIdentityResolution.Resolved ids,
        CancellationToken cancellationToken
    )
    {
        var token = await appTokens.GetAccessTokenAsync(cancellationToken);
        var context = new HelixRequestContext(identity.ClientId, token);
        if (item.ReconcileOnly)
        {
            return await ReconcileAsync(item, ids, context, cancellationToken);
        }

        if (item.IsUnpin)
        {
            var current = await pins.GetAsync(
                context,
                ids.BroadcasterId,
                ids.BotUserId,
                cancellationToken
            );
            if (current is not ChatPinnedMessageResult.Found found)
            {
                return current switch
                {
                    ChatPinnedMessageResult.Absent => new PublicChatPinExecutionOutcome.NoOp(
                        "already-absent"
                    ),
                    ChatPinnedMessageResult.PermissionDenied =>
                        new PublicChatPinExecutionOutcome.Terminal("permission-denied"),
                    ChatPinnedMessageResult.RateLimited =>
                        new PublicChatPinExecutionOutcome.Terminal("rate-limited"),
                    _ => new PublicChatPinExecutionOutcome.Terminal("read-unavailable"),
                };
            }

            if (found.MessageId != item.TwitchMessageId || found.PinnedByUserId != ids.BotUserId)
            {
                return new PublicChatPinExecutionOutcome.NoOp("replaced-or-not-bot-owned");
            }

            var unpin = await pins.UnpinAsync(
                context,
                ids.BroadcasterId,
                ids.BotUserId,
                item.TwitchMessageId,
                cancellationToken
            );
            return await ClassifyUnpinAsync(item, ids, context, unpin, cancellationToken);
        }

        var pin = await pins.PinAsync(
            context,
            ids.BroadcasterId,
            ids.BotUserId,
            item.TwitchMessageId,
            item.DurationSeconds,
            cancellationToken
        );
        return pin switch
        {
            ChatPinMutationResult.Succeeded => new PublicChatPinExecutionOutcome.Pinned(
                ids.BotUserId
            ),
            ChatPinMutationResult.Conflict or ChatPinMutationResult.Ambiguous =>
                await ReconcilePinMutationAsync(item, ids, context, pin, cancellationToken),
            ChatPinMutationResult.PermissionDenied => new PublicChatPinExecutionOutcome.Terminal(
                "permission-denied"
            ),
            ChatPinMutationResult.RateLimited => new PublicChatPinExecutionOutcome.Terminal(
                "rate-limited"
            ),
            ChatPinMutationResult.NotFound => new PublicChatPinExecutionOutcome.Terminal(
                "message-not-found"
            ),
            ChatPinMutationResult.Invalid => new PublicChatPinExecutionOutcome.Terminal(
                "invalid-request"
            ),
            _ => new PublicChatPinExecutionOutcome.Terminal("unexpected"),
        };
    }

    private async ValueTask<PublicChatPinExecutionOutcome> ReconcilePinMutationAsync(
        PublicChatPinWorkItem item,
        ChatIdentityResolution.Resolved ids,
        HelixRequestContext context,
        ChatPinMutationResult mutation,
        CancellationToken cancellationToken
    )
    {
        var reconciled = await ReconcileCurrentPinAsync(item, ids, context, cancellationToken);
        if (reconciled is PublicChatPinExecutionOutcome.Pinned)
        {
            return reconciled;
        }

        return new PublicChatPinExecutionOutcome.Terminal(
            mutation is ChatPinMutationResult.Conflict ? "conflict" : "ambiguous"
        );
    }

    private async ValueTask<PublicChatPinExecutionOutcome> ReconcileAsync(
        PublicChatPinWorkItem item,
        ChatIdentityResolution.Resolved ids,
        HelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var current = await pins.GetAsync(
            context,
            ids.BroadcasterId,
            ids.BotUserId,
            cancellationToken
        );
        if (!item.IsUnpin)
        {
            return
                current is ChatPinnedMessageResult.Found found
                && found.MessageId == item.TwitchMessageId
                && found.PinnedByUserId == ids.BotUserId
                ? new PublicChatPinExecutionOutcome.Pinned(ids.BotUserId)
                : new PublicChatPinExecutionOutcome.Terminal("ambiguous-after-restart");
        }

        return
            current is ChatPinnedMessageResult.Found foundPin
            && foundPin.MessageId == item.TwitchMessageId
            && foundPin.PinnedByUserId == ids.BotUserId
            ? new PublicChatPinExecutionOutcome.Terminal("unpin-ambiguous-after-restart")
            : new PublicChatPinExecutionOutcome.NoOp("unpin-reconciled");
    }

    private async ValueTask<PublicChatPinExecutionOutcome> ReconcileCurrentPinAsync(
        PublicChatPinWorkItem item,
        ChatIdentityResolution.Resolved ids,
        HelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        var current = await pins.GetAsync(
            context,
            ids.BroadcasterId,
            ids.BotUserId,
            cancellationToken
        );
        return
            current is ChatPinnedMessageResult.Found found
            && found.MessageId == item.TwitchMessageId
            && found.PinnedByUserId == ids.BotUserId
            ? new PublicChatPinExecutionOutcome.Pinned(ids.BotUserId)
            : new PublicChatPinExecutionOutcome.Terminal("not-exact-bot-pin");
    }

    private async ValueTask<PublicChatPinExecutionOutcome> ClassifyUnpinAsync(
        PublicChatPinWorkItem item,
        ChatIdentityResolution.Resolved ids,
        HelixRequestContext context,
        ChatPinMutationResult mutation,
        CancellationToken cancellationToken
    )
    {
        if (mutation is ChatPinMutationResult.Succeeded)
        {
            return new PublicChatPinExecutionOutcome.Unpinned();
        }

        if (mutation is ChatPinMutationResult.NotFound)
        {
            return new PublicChatPinExecutionOutcome.NoOp("already-absent");
        }

        if (mutation is ChatPinMutationResult.Ambiguous)
        {
            var current = await pins.GetAsync(
                context,
                ids.BroadcasterId,
                ids.BotUserId,
                cancellationToken
            );
            if (
                current is not ChatPinnedMessageResult.Found found
                || found.MessageId != item.TwitchMessageId
                || found.PinnedByUserId != ids.BotUserId
            )
            {
                return new PublicChatPinExecutionOutcome.NoOp("unpin-reconciled");
            }
        }

        return new PublicChatPinExecutionOutcome.Terminal(
            mutation switch
            {
                ChatPinMutationResult.PermissionDenied => "permission-denied",
                ChatPinMutationResult.RateLimited => "rate-limited",
                ChatPinMutationResult.Invalid => "invalid-request",
                ChatPinMutationResult.Conflict => "conflict",
                _ => "ambiguous",
            }
        );
    }
}
