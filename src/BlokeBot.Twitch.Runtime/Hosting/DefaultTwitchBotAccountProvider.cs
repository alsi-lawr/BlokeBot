namespace BlokeBot.Twitch.Runtime;

internal sealed class DefaultTwitchBotAccountProvider(
    TwitchBotSettings settings,
    ITwitchAccessTokenProvider tokens
) : ITwitchBotAccountProvider
{
    public async ValueTask<TwitchBotAccount> GetBotAccountAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        return new(
            TwitchLogin.Normalize(settings.Identity.BotUsername),
            await tokens.GetAccessTokenAsync(cancellationToken)
        );
    }
}
