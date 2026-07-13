namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record ChannelBotAuthorizationResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> MissingScopes
)
{
    public static ChannelBotAuthorizationResult Success(string message)
    {
        return new(true, message, []);
    }

    public static ChannelBotAuthorizationResult Failure(
        string message,
        IReadOnlyList<string>? missingScopes = null
    )
    {
        return new(false, message, missingScopes ?? []);
    }
}
