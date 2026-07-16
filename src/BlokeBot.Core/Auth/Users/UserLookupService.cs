using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Functional;

namespace BlokeBot.Core.Auth.Users;

internal sealed class UserLookupService(
    WebAuthConfiguration configuration,
    IAccessTokenProvider tokens,
    HelixClient helix
)
{
    public IO<Option<UserIdentity>, AccessTokenUnavailableReason> FindByLogin(string login)
    {
        return IO<Option<UserIdentity>, AccessTokenUnavailableReason>.Create(
            async cancellationToken =>
            {
                var accessToken = await tokens.GetAccessToken().ExecuteAsync(cancellationToken);
                return await accessToken.Match(
                    async token =>
                        Result<Option<UserIdentity>, AccessTokenUnavailableReason>.Success(
                            await FindByLoginAsync(token, login, cancellationToken)
                        ),
                    reason =>
                        Task.FromResult(
                            Result<Option<UserIdentity>, AccessTokenUnavailableReason>.Error(reason)
                        )
                );
            }
        );
    }

    private async Task<Option<UserIdentity>> FindByLoginAsync(
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
            new HelixRequestContext(configuration.Identity.ClientId, accessToken),
            [normalized],
            cancellationToken
        );
        return ToIdentity(users.FirstOrDefault());
    }

    public async Task<Option<UserIdentity>> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var user = await helix.GetCurrentUserAsync(
            new HelixRequestContext(configuration.Identity.ClientId, accessToken),
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
}
