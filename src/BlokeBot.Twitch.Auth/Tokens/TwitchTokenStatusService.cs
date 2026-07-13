using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Twitch.Auth;

public sealed class TwitchTokenStatusService(IServiceProvider services, TwitchOAuthApiClient oauth)
{
    public async Task<TwitchTokenStatus> GetUserAccessTokenStatusAsync(
        IEnumerable<string?> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        var required = TwitchScopeSet.NormalizeMany(requiredScopes);
        var provider = services.GetService<ITwitchAccessTokenProvider>();
        if (provider is null)
        {
            return Unavailable(required);
        }

        string accessToken;
        try
        {
            accessToken = await provider.GetAccessTokenAsync(cancellationToken);
        }
        catch (TwitchAccessTokenUnavailableException)
        {
            return Unavailable(required);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Unknown(required);
        }

        try
        {
            var validation = await oauth.ValidateTokenAsync(accessToken, cancellationToken);
            if (validation is null)
            {
                return Invalid(accessToken, required);
            }

            var granted = TwitchScopeSet.NormalizeMany(validation.Scopes);
            var missing = TwitchScopeSet.Missing(granted, required);
            var state =
                missing.Length == 0
                    ? TwitchTokenStatusState.Ready
                    : TwitchTokenStatusState.MissingScopes;

            return new TwitchTokenStatus(
                state,
                accessToken,
                validation,
                required,
                granted,
                missing
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Unknown(required, accessToken);
        }
    }

    private static TwitchTokenStatus Unknown(string[] required, string? accessToken = null)
    {
        return new(TwitchTokenStatusState.Unknown, accessToken, null, required, [], required);
    }

    private static TwitchTokenStatus Unavailable(string[] required)
    {
        return new(TwitchTokenStatusState.Unavailable, null, null, required, [], required);
    }

    private static TwitchTokenStatus Invalid(string accessToken, string[] required)
    {
        return new(TwitchTokenStatusState.Invalid, accessToken, null, required, [], required);
    }
}
