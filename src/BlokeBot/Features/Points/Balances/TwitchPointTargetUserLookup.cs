using Microsoft.Extensions.Options;

namespace BlokeBot.Features.Points.Balances;

public sealed class TwitchPointTargetUserLookup(
    TwitchAppAccessTokenProvider appTokens,
    TwitchHelixApiClient helix,
    IOptions<TwitchBotOptions> options
) : IPointTargetUserLookup
{
    public async Task<bool> ExistsAsync(string login, CancellationToken ct)
    {
        var normalized = TwitchLogin.Normalize(login);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(options.Value.Identity.ClientId))
            return false;

        var accessToken = await appTokens.GetAccessTokenAsync(ct);
        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(options.Value.Identity.ClientId, accessToken),
            [normalized],
            ct
        );

        return users.Any(user =>
            string.Equals(
                TwitchLogin.Normalize(user.Login),
                normalized,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }
}
