namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record ActiveBotAccountTokenStatus(
    string BotLogin,
    string? ProfileImageUrl,
    TwitchTokenStatusState State,
    string? AccessToken,
    TwitchTokenValidation? Validation,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<string> MissingScopes
)
{
    public static ActiveBotAccountTokenStatus FromStatus(
        string botLogin,
        string? profileImageUrl,
        TwitchTokenStatus status
    ) =>
        new(
            botLogin,
            profileImageUrl,
            status.State,
            status.AccessToken,
            status.Validation,
            status.RequiredScopes,
            status.GrantedScopes,
            status.MissingScopes
        );
}
