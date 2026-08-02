namespace BlokeBot.Twitch.Runtime;

internal sealed class ChatIdentityResolver(BotIdentity identity, HelixClient helix)
{
    internal async Task<string?> ResolveBroadcasterIdAsync(
        string channelLogin,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var channel = Login.Normalize(channelLogin);
        var users = await helix.GetUsersByLoginAsync(
            new HelixRequestContext(identity.ClientId, accessToken),
            [channel],
            cancellationToken
        );
        return users
            .FirstOrDefault(user => user.Login.Equals(channel, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    internal async Task<ChatIdentityResolution> ResolveAsync(
        string channelLogin,
        string botLogin,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var channel = Login.Normalize(channelLogin);
        var bot = Login.Normalize(botLogin);
        var users = await helix.GetUsersByLoginAsync(
            new HelixRequestContext(identity.ClientId, accessToken),
            [channel, bot],
            cancellationToken
        );
        var broadcaster = users.FirstOrDefault(user =>
            user.Login.Equals(channel, StringComparison.OrdinalIgnoreCase)
        );
        if (string.IsNullOrWhiteSpace(broadcaster?.Id))
        {
            return new ChatIdentityResolution.MissingChannel();
        }

        var botUser = users.FirstOrDefault(user =>
            user.Login.Equals(bot, StringComparison.OrdinalIgnoreCase)
        );
        return string.IsNullOrWhiteSpace(botUser?.Id)
            ? new ChatIdentityResolution.MissingBot()
            : new ChatIdentityResolution.Resolved
            {
                BroadcasterId = broadcaster.Id,
                BotUserId = botUser.Id,
            };
    }
}

internal abstract record ChatIdentityResolution
{
    private ChatIdentityResolution() { }

    internal abstract TResult Match<TResult>(
        Func<Resolved, TResult> resolved,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot
    );

    internal sealed record Resolved : ChatIdentityResolution
    {
        internal required string BroadcasterId { get; init; }

        internal required string BotUserId { get; init; }

        internal override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot
        ) => resolved(this);
    }

    internal sealed record MissingChannel : ChatIdentityResolution
    {
        internal override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot
        ) => missingChannel(this);
    }

    internal sealed record MissingBot : ChatIdentityResolution
    {
        internal override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot
        ) => missingBot(this);
    }
}
