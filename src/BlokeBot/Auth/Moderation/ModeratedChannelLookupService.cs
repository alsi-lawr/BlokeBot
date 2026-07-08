using Alsi.TwitchBot;
using BlokeBot.Auth.OAuth;

namespace BlokeBot.Auth.Moderation;

internal sealed class ModeratedChannelLookupService(TwitchHelixApiClient helix)
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
            new TwitchHelixRequestContext(options.ClientId, accessToken),
            userId,
            ct
        );
        var ownLogin = TwitchLogin.Normalize(userLogin);
        return moderatedChannels
            .Select(channel => TwitchLogin.Normalize(channel.BroadcasterLogin))
            .Where(login =>
                login.Length > 0
                && !string.Equals(login, ownLogin, StringComparison.OrdinalIgnoreCase)
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
