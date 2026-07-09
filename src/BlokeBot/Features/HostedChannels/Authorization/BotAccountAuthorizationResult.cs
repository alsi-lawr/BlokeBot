namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record BotAccountAuthorizationResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> MissingScopes
)
{
    public static BotAccountAuthorizationResult Success(string message) => new(true, message, []);

    public static BotAccountAuthorizationResult Failure(
        string message,
        IReadOnlyList<string>? missingScopes = null
    ) => new(false, message, missingScopes ?? []);
}
