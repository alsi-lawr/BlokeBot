using BlokeBot.Auth.OAuth;
using BlokeBot.Functional;

namespace BlokeBot.Auth.Users;

internal sealed class UserLookupService(
    WebAuthConfiguration configuration,
    ITwitchAccessTokenProvider tokens,
    HelixClient helix
)
{
    public async Task<Option<UserIdentity>> FindByLoginAsync(
        string login,
        CancellationToken cancellationToken
    )
    {
        return await FindByLoginAsync(
            CreateCurrentOptions(),
            await tokens.GetAccessTokenAsync(cancellationToken),
            login,
            cancellationToken
        );
    }

    private async Task<Option<UserIdentity>> FindByLoginAsync(
        WebAuthOptions options,
        string accessToken,
        string login,
        CancellationToken cancellationToken
    )
    {
        var normalized = Login.Normalize(login);
        if (normalized.Length == 0)
        {
            return Option<UserIdentity>.None;
        }

        var users = await helix.GetUsersByLoginAsync(
            new HelixRequestContext(options.ClientId, accessToken),
            [normalized],
            cancellationToken
        );
        return ToIdentity(users.FirstOrDefault());
    }

    public async Task<Option<UserIdentity>> GetCurrentUserAsync(
        WebAuthOptions options,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var user = await helix.GetCurrentUserAsync(
            new HelixRequestContext(options.ClientId, accessToken),
            cancellationToken
        );

        return ToIdentity(user);
    }

    private static Option<UserIdentity> ToIdentity(HelixUser? user)
    {
        return user is null
            ? Option<UserIdentity>.None
            : UserIdentity.Create(user.Id, user.Login, user.DisplayName, user.ProfileImageUrl);
    }

    private WebAuthOptions CreateCurrentOptions()
    {
        return configuration.CurrentOptions;
    }
}
