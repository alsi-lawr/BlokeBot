using Microsoft.Extensions.Options;

namespace BlokeBot.Auth.OAuth;

internal sealed class WebAuthConfiguration(
    IOptions<WebAuthOptions> options,
    IConfiguration configuration
)
{
    public WebAuthOptions CurrentOptions
    {
        get
        {
            var configured = options.Value;
            var identity = configuration.GetSection("TwitchBot:Identity");

            return new WebAuthOptions
            {
                CallbackPath = First(configured.CallbackPath, "/auth/twitch/callback"),
                ClientId = First(configured.ClientId, identity["ClientId"]),
                ClientSecret = First(configured.ClientSecret, identity["ClientSecret"]),
                CookieName = First(configured.CookieName, "BlokeBot.Auth"),
            };
        }
    }

    public bool IsConfigured(WebAuthOptions currentOptions)
    {
        return !string.IsNullOrWhiteSpace(currentOptions.ClientId)
            && !string.IsNullOrWhiteSpace(currentOptions.ClientSecret)
            && !string.IsNullOrWhiteSpace(currentOptions.CallbackPath);
    }

    private static string First(string? configuredValue, string? fallbackValue)
    {
        return !string.IsNullOrWhiteSpace(configuredValue)
            ? configuredValue
            : fallbackValue ?? string.Empty;
    }
}
