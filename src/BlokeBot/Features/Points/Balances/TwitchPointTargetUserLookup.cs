namespace BlokeBot.Features.Points.Balances;

public sealed class TwitchPointTargetUserLookup(
    TwitchAppAccessTokenProvider appTokens,
    TwitchHelixApiClient helix,
    TwitchBotSettings settings
) : IPointTargetUserLookup
{
    public async Task<bool> ExistsAsync(string login, CancellationToken ct)
    {
        var normalized = TwitchLogin.Normalize(login);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(settings.Identity.ClientId))
            return false;

        var accessToken = await appTokens.GetAccessTokenAsync(ct);
        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(settings.Identity.ClientId, accessToken),
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
