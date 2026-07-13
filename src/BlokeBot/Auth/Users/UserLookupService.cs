using BlokeBot.Auth.OAuth;
using BlokeBot.Functional;

namespace BlokeBot.Auth.Users;

internal sealed class UserLookupService(
    WebAuthConfiguration configuration,
    ITwitchAccessTokenProvider tokens,
    TwitchHelixApiClient helix
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
        var normalized = TwitchLogin.Normalize(login);
        if (normalized.Length == 0)
        {
            return Option<UserIdentity>.None;
        }

        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(options.ClientId, accessToken),
            [normalized],
            cancellationToken
        );
        return Option<TwitchHelixUser>.FromNullable(users.FirstOrDefault()).Map(ToIdentity);
    }

    public async Task<Option<UserIdentity>> GetCurrentUserAsync(
        WebAuthOptions options,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var user = await helix.GetCurrentUserAsync(
            new TwitchHelixRequestContext(options.ClientId, accessToken),
            cancellationToken
        );

        return string.IsNullOrWhiteSpace(user?.Id) || string.IsNullOrWhiteSpace(user.Login)
            ? Option<UserIdentity>.None
            : Option<UserIdentity>.Some(ToIdentity(user));
    }

    private static UserIdentity ToIdentity(TwitchHelixUser user)
    {
        return new UserIdentity
        {
            Id = user.Id,
            Login = user.Login,
            DisplayName = user.DisplayName,
            ProfileImageUrl = user.ProfileImageUrl,
        };
    }

    private WebAuthOptions CreateCurrentOptions()
    {
        return configuration.CurrentOptions;
    }
}
