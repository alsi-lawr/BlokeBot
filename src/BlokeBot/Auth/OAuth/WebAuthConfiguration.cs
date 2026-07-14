using Microsoft.Extensions.Options;

namespace BlokeBot.Auth.OAuth;

internal sealed class WebAuthConfiguration(
    IOptions<WebAuthOptions> options,
    BotSettings botSettings
)
{
    public BotIdentity Identity => botSettings.Identity;

    public WebAuthOptions CurrentOptions
    {
        get
        {
            var configured = options.Value;

            return new WebAuthOptions
            {
                CallbackPath = First(configured.CallbackPath, "/auth/twitch/callback"),
                CookieName = First(configured.CookieName, "BlokeBot.Auth"),
            };
        }
    }

    public bool IsConfigured(WebAuthOptions currentOptions)
    {
        return !string.IsNullOrWhiteSpace(Identity.ClientId)
            && !string.IsNullOrWhiteSpace(Identity.ClientSecret)
            && !string.IsNullOrWhiteSpace(currentOptions.CallbackPath);
    }

    private static string First(string? configuredValue, string? fallbackValue)
    {
        return !string.IsNullOrWhiteSpace(configuredValue)
            ? configuredValue
            : fallbackValue ?? string.Empty;
    }
}
