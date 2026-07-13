namespace BlokeBot.Features.AccessLists;

internal sealed class TwitchAccessListProfileEnrichmentPolicy(
    TwitchAppAccessTokenProvider appTokens,
    HelixClient helix,
    TwitchBotIdentity identity
) : IAccessListProfileEnrichmentPolicy
{
    public async Task<IReadOnlyList<AccessListEntryProfile>> EnrichAsync(
        IReadOnlyList<string> logins,
        CancellationToken cancellationToken
    )
    {
        var token = await appTokens.GetAccessTokenAsync(cancellationToken);
        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(identity.ClientId, token),
            logins,
            cancellationToken
        );
        var profileImages = users.ToDictionary(
            user => TwitchLogin.Normalize(user.Login),
            user => user.ProfileImageUrl,
            StringComparer.OrdinalIgnoreCase
        );

        return
        [
            .. logins.Select(login =>
            {
                profileImages.TryGetValue(TwitchLogin.Normalize(login), out var profileImageUrl);
                return new AccessListEntryProfile(
                    login,
                    string.IsNullOrWhiteSpace(profileImageUrl) ? null : profileImageUrl
                );
            }),
        ];
    }
}
