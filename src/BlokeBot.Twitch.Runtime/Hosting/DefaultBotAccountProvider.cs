namespace BlokeBot.Twitch.Runtime;

internal sealed class DefaultBotAccountProvider(BotSettings settings, IAccessTokenProvider tokens)
    : IBotAccountProvider
{
    public async ValueTask<BotAccount> GetBotAccountAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        return new(
            Login.Normalize(settings.Identity.BotUsername),
            await tokens.GetAccessTokenAsync(cancellationToken)
        );
    }
}
