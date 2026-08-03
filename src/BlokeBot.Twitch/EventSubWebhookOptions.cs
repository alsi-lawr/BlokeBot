namespace BlokeBot.Twitch;

/// <summary>Configuration for Twitch's direct EventSub webhook transport.</summary>
public sealed class EventSubWebhookOptions
{
    public required Uri CallbackUri { get; init; }

    public required string Secret { get; init; }

    public void Validate(bool online = true)
    {
        ArgumentNullException.ThrowIfNull(CallbackUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(Secret);
        if (Secret.Length is < 10 or > 100 || Secret.Any(static c => c is < ' ' or > '~'))
        {
            throw new InvalidOperationException(
                "The EventSub webhook secret must contain 10-100 printable ASCII characters."
            );
        }

        var isProductionHttps =
            CallbackUri.IsAbsoluteUri
            && CallbackUri.Scheme == Uri.UriSchemeHttps
            && CallbackUri.Port == 443
            && string.IsNullOrEmpty(CallbackUri.UserInfo)
            && string.IsNullOrEmpty(CallbackUri.Query)
            && string.IsNullOrEmpty(CallbackUri.Fragment);
        if (online)
        {
            if (!isProductionHttps)
            {
                throw new InvalidOperationException(
                    "The EventSub webhook callback must be an absolute HTTPS URI on port 443."
                );
            }

            return;
        }

        if (!IsLoopbackHttp(CallbackUri))
        {
            throw new InvalidOperationException(
                "The simulator EventSub callback must use an explicit loopback HTTP URI."
            );
        }
    }

    private static bool IsLoopbackHttp(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.IsLoopback
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    public override string ToString() =>
        "EventSubWebhookOptions { CallbackUri = [redacted], Secret = [redacted] }";
}

/// <summary>Supplies an app access token for EventSub management.</summary>
public interface IAppAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
