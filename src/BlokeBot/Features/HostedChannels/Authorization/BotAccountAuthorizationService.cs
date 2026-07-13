namespace BlokeBot.Features.HostedChannels.Authorization;

public enum BotAccountAuthorizationState
{
    Disabled,
    Unknown,
    NotAuthorized,
    WrongAccount,
    MissingScopes,
    Ready,
}

public sealed record BotAccountAuthorizationStatus(
    string? ConfiguredBotLogin,
    string? AuthorizedLogin,
    string? AuthorizedProfileImageUrl,
    BotAccountAuthorizationState State,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<string> MissingScopes,
    string Message
);

public sealed class BotAccountAuthorizationService(IBotAccountAuthorizationPolicy policy)
{
    public Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken ct)
    {
        return policy.GetStatusAsync(ct);
    }

    public Task ClearAsync(CancellationToken ct)
    {
        return policy.ClearAsync(ct);
    }
}
