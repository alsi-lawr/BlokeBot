using BlokeBot.Auth.OAuth;

namespace BlokeBot.Auth.Moderation;

internal sealed class ModeratedChannelLookupService(HelixClient helix)
{
    public async Task<IReadOnlyList<string>> LoadModeratedLoginsAsync(
        WebAuthOptions options,
        string accessToken,
        string userId,
        string userLogin,
        CancellationToken ct
    )
    {
        var moderatedChannels = await helix.GetModeratedChannelsAsync(
            new HelixRequestContext(options.ClientId, accessToken),
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
