namespace BlokeBot.Features.Points.Balances;

public sealed class TwitchPointTargetUserLookup(
    AppAccessTokenProvider appTokens,
    HelixClient helix,
    TwitchBotSettings settings
) : IPointTargetUserLookup
{
    public async Task<bool> ExistsAsync(string login, CancellationToken ct)
    {
        var normalized = Login.Normalize(login);
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(settings.Identity.ClientId))
        {
            return false;
        }

        var accessToken = await appTokens.GetAccessTokenAsync(ct);
        var users = await helix.GetUsersByLoginAsync(
            new HelixRequestContext(settings.Identity.ClientId, accessToken),
            [normalized],
            ct
        );

        return users.Any(user =>
            string.Equals(
                Login.Normalize(user.Login),
                normalized,
                StringComparison.OrdinalIgnoreCase
            )
        );
    }
}
