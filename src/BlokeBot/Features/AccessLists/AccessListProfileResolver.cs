using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.AccessLists;

public sealed class AccessListProfileResolver(
    IServiceProvider services,
    TwitchHelixApiClient helix,
    IOptions<TwitchBotOptions> options
)
{
    public async Task<IReadOnlyList<AccessListEntryProfile>> ResolveAsync(
        IEnumerable<string> logins,
        CancellationToken ct
    )
    {
        var entries = logins
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .Select(login => login.Trim())
            .ToArray();
        if (entries.Length == 0)
            return [];

        var appTokens = services.GetService<TwitchAppAccessTokenProvider>();
        var clientId = options.Value.Identity.ClientId;
        if (appTokens is null || string.IsNullOrWhiteSpace(clientId))
            return entries.Select(login => new AccessListEntryProfile(login, null)).ToArray();

        var token = await appTokens.GetAccessTokenAsync(ct);
        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(clientId, token),
            entries,
            ct
        );
        var profileImages = users.ToDictionary(
            user => TwitchLogin.Normalize(user.Login),
            user => user.ProfileImageUrl,
            StringComparer.OrdinalIgnoreCase
        );

        return entries
            .Select(login =>
            {
                profileImages.TryGetValue(TwitchLogin.Normalize(login), out var profileImageUrl);
                return new AccessListEntryProfile(
                    login,
                    string.IsNullOrWhiteSpace(profileImageUrl) ? null : profileImageUrl
                );
            })
            .ToArray();
    }
}
