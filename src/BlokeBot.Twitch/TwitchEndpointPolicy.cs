namespace BlokeBot.Twitch;

/// <summary>
/// Supplies the provider endpoints used by Twitch OAuth, Helix, and EventSub transports.
/// </summary>
public sealed class TwitchEndpointPolicy
{
    public const string ConfigurationSectionName = "TwitchEndpoints";

    public static TwitchEndpointPolicy Default => new();

    public Uri OAuthOrigin { get; set; } = new("https://id.twitch.tv/oauth2/");

    public Uri HelixOrigin { get; set; } = new("https://api.twitch.tv/helix/");

    public Uri EventSubWebSocketUri { get; set; } = new("wss://eventsub.wss.twitch.tv/ws");

    public Uri InitialEventSubWebSocketEndpoint
    {
        get
        {
            ValidateWebSocketEndpoint(EventSubWebSocketUri);
            return EventSubWebSocketUri;
        }
    }

    public Uri OAuthAuthorizationEndpoint => CreateHttpEndpoint(OAuthOrigin, "authorize");

    public Uri OAuthTokenEndpoint => CreateHttpEndpoint(OAuthOrigin, "token");

    public Uri OAuthValidationEndpoint => CreateHttpEndpoint(OAuthOrigin, "validate");

    public Uri HelixEndpoint(string path) => CreateHttpEndpoint(HelixOrigin, path);

    public void Validate()
    {
        _ = OAuthAuthorizationEndpoint;
        _ = OAuthTokenEndpoint;
        _ = OAuthValidationEndpoint;
        _ = HelixEndpoint("health");
        _ = InitialEventSubWebSocketEndpoint;
    }

    private static Uri CreateHttpEndpoint(Uri origin, string path)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/'))
        {
            throw new ArgumentException("The endpoint path must be relative.", nameof(path));
        }

        if (
            !origin.IsAbsoluteUri
            || origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
        )
        {
            throw new InvalidOperationException(
                "Twitch HTTP origins must be absolute HTTP or HTTPS URIs without a query or fragment."
            );
        }

        var baseUri = origin.AbsoluteUri.EndsWith('/') ? origin : new Uri(origin.AbsoluteUri + "/");
        return new Uri(baseUri, path);
    }

    private static void ValidateWebSocketEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (
            !endpoint.IsAbsoluteUri
            || endpoint.Scheme is not "ws" and not "wss"
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
        )
        {
            throw new InvalidOperationException(
                "The EventSub WebSocket endpoint must be an absolute WS or WSS URI without a query or fragment."
            );
        }
    }
}
