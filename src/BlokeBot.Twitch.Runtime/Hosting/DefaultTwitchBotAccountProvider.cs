using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Runtime;

internal sealed class DefaultTwitchBotAccountProvider(
    IOptions<TwitchBotOptions> options,
    ITwitchAccessTokenProvider tokens
) : ITwitchBotAccountProvider
{
    private readonly TwitchBotOptions options = options.Value;

    public async ValueTask<TwitchBotAccount> GetBotAccountAsync(
        string channelLogin,
        CancellationToken cancellationToken
    ) =>
        new(
            TwitchLogin.Normalize(options.Identity.BotUsername),
            await tokens.GetAccessTokenAsync(cancellationToken)
        );
}
