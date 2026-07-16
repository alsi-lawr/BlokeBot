using BlokeBot.Core.Auth.OAuth;

namespace BlokeBot.Core.Auth.Moderation;

internal sealed class ModeratedChannelLookupService(
    WebAuthConfiguration configuration,
    HelixClient helix
)
{
    public async Task<IReadOnlyList<string>> LoadModeratedLoginsAsync(
        string accessToken,
        string userId,
        string userLogin,
        CancellationToken ct
    )
    {
        var moderatedChannels = await helix.GetModeratedChannelsAsync(
            new HelixRequestContext(configuration.Identity.ClientId, accessToken),
            userId,
            ct
        );
        var ownLogin = Login.Normalize(userLogin);
        return moderatedChannels
            .Select(channel => Login.Normalize(channel.BroadcasterLogin))
            .Where(login =>
                login.Length > 0
                && !string.Equals(login, ownLogin, StringComparison.OrdinalIgnoreCase)
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
