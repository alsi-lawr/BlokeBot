using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface IPublicChatTransport
{
    ValueTask<PublicChatPreparationOutcome> PrepareAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    );

    ValueTask<PublicChatTransportSendResult> SendAsync(
        PublicChatPreparedSend prepared,
        CancellationToken cancellationToken
    );
}

internal sealed class HelixPublicChatTransport(
    AppAccessTokenProvider appTokens,
    ITwitchBotAccountProvider botAccounts,
    BotIdentity identity,
    ChatIdentityResolver identities,
    ChatClient chat,
    ILogger<HelixPublicChatTransport> log
) : IPublicChatTransport
{
    public async ValueTask<PublicChatPreparationOutcome> PrepareAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var botAccount = await botAccounts.GetBotAccountAsync(
                message.Channel,
                cancellationToken
            );
            var resolution = await identities.ResolveAsync(
                message.Channel,
                botAccount.Login,
                botAccount.AccessToken,
                cancellationToken
            );
            return await resolution.Match(
                resolved => PrepareResolvedAsync(message, resolved, cancellationToken),
                static _ =>
                    ValueTask.FromResult<PublicChatPreparationOutcome>(
                        new PublicChatPreparationOutcome.MissingChannel()
                    ),
                static _ =>
                    ValueTask.FromResult<PublicChatPreparationOutcome>(
                        new PublicChatPreparationOutcome.MissingBot()
                    )
            );
        }
        catch (Exception exception)
        {
            return PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                exception,
                cancellationToken
            );
        }
    }

    public async ValueTask<PublicChatTransportSendResult> SendAsync(
        PublicChatPreparedSend prepared,
        CancellationToken cancellationToken
    )
    {
        var result = await chat.SendMessageAsync(
            new HelixRequestContext(identity.ClientId, prepared.AppAccessToken),
            prepared.BroadcasterId,
            prepared.BotUserId,
            prepared.Message.Message,
            cancellationToken
        );
        var classified = PublicChatDeliveryClassifier.ClassifySendResult(result);
        classified.Match(
            _ =>
                log.LogInformation(
                    "Sent public chat outbox message {OutboxMessageId} via Helix in #{Channel}.",
                    prepared.Message.Id,
                    prepared.Message.Channel
                ),
            static _ => { }
        );
        return classified;
    }

    private async ValueTask<PublicChatPreparationOutcome> PrepareResolvedAsync(
        PublicChatClaimedMessage message,
        ChatIdentityResolution.Resolved identities,
        CancellationToken cancellationToken
    )
    {
        var appAccessToken = await appTokens.GetAccessTokenAsync(cancellationToken);
        return new PublicChatPreparationOutcome.Ready
        {
            Send = new PublicChatPreparedSend
            {
                Message = message,
                AppAccessToken = appAccessToken,
                BroadcasterId = identities.BroadcasterId,
                BotUserId = identities.BotUserId,
            },
        };
    }
}
