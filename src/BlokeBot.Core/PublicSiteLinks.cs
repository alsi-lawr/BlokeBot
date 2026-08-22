using Microsoft.Extensions.Options;

namespace BlokeBot.Core;

internal sealed class PublicSiteLinks
{
    private readonly Uri _baseAddress;

    public PublicSiteLinks(IOptions<BlokeBotOptions> options, BotSettings twitch)
    {
        var configured = options.Value.PublicBaseUrl;
        _baseAddress = string.IsNullOrWhiteSpace(configured)
            ? RedirectOrigin(twitch.Identity.RedirectUri)
            : ConfiguredBaseAddress(configured)
                ?? throw new OptionsValidationException(
                    "BlokeBot",
                    typeof(BlokeBotOptions),
                    [BlokeBotOptionsValidation.PublicBaseUrlFailure]
                );
    }

    public Uri Resolve(string path) => new(_baseAddress, path.TrimStart('/'));

    internal static bool HasValidConfiguredBaseAddress(string? configured) =>
        string.IsNullOrWhiteSpace(configured) || ConfiguredBaseAddress(configured) is not null;

    private static Uri? ConfiguredBaseAddress(string configured) =>
        !Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
        || uri.Scheme is not ("http" or "https")
        || !string.IsNullOrEmpty(uri.UserInfo)
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment)
            ? null
        : uri.AbsolutePath.EndsWith('/') ? uri
        : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");

    private static Uri RedirectOrigin(string configured) =>
        Uri.TryCreate(configured, UriKind.Absolute, out var redirect)
        && redirect.Scheme is "http" or "https"
        && string.IsNullOrEmpty(redirect.UserInfo)
            ? new Uri(redirect.GetLeftPart(UriPartial.Authority) + "/")
            : throw new OptionsValidationException(
                "TwitchBot",
                typeof(BotIdentityOptions),
                ["TwitchBot:Identity:RedirectUri must be an absolute HTTP or HTTPS URL."]
            );
}
