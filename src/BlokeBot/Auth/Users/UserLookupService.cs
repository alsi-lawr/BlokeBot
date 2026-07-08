using Alsi.TwitchBot;
using BlokeBot.Auth.OAuth;

namespace BlokeBot.Auth.Users;

internal sealed class UserLookupService(
    WebAuthConfiguration configuration,
    ITwitchAccessTokenProvider tokens,
    TwitchHelixApiClient helix
)
{
    public async Task<TwitchHelixUser?> FindByLoginAsync(
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

    public async Task<TwitchHelixUser?> FindByLoginAsync(
        WebAuthOptions options,
        string accessToken,
        string login,
        CancellationToken cancellationToken
    )
    {
        var normalized = TwitchLogin.Normalize(login);
        if (normalized.Length == 0)
            return null;

        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(options.ClientId, accessToken),
            [normalized],
            cancellationToken
        );
        return users.FirstOrDefault();
    }

    public async Task<TwitchHelixUser?> GetCurrentUserAsync(
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
            ? null
            : user;
    }

    private WebAuthOptions CreateCurrentOptions() => configuration.CurrentOptions;
}
