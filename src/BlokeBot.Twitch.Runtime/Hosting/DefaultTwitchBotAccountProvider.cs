namespace BlokeBot.Twitch.Runtime;

internal sealed class DefaultTwitchBotAccountProvider(
    TwitchBotSettings settings,
    IAccessTokenProvider tokens
) : ITwitchBotAccountProvider
{
    public async ValueTask<TwitchBotAccount> GetBotAccountAsync(
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
