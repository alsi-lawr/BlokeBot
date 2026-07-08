namespace BlokeBot.Auth.OAuth;

public sealed class WebAuthOptions
{
    public string CallbackPath { get; set; } = "/auth/twitch/callback";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string CookieName { get; set; } = "BlokeBot.Auth";
}
