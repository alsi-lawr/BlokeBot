namespace BlokeBot.Site;

public sealed record BlokeBotSiteOptions
{
    public string? LiveAppUrl { get; init; }

    public string? ControllerName { get; init; }

    public string? PrivacyContact { get; init; }

    public string? PrivacyNoticeUrl { get; init; }

    internal Uri? LiveAppUri =>
        Uri.TryCreate(LiveAppUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : null;
}

internal static class BlokeBotSiteOptionsValidation
{
    internal const string LiveAppUrlFailure =
        "BlokeBotSite:LiveAppUrl must be an absolute HTTP or HTTPS URL when configured.";

    internal const string PrivacyConfigurationFailure =
        "Online deployments require BlokeBotSite:ControllerName, a monitored "
        + "BlokeBotSite:PrivacyContact email address, and an absolute HTTPS "
        + "BlokeBotSite:PrivacyNoticeUrl. Supply the deployment's own values; there is no default.";

    internal static bool HasValidLiveAppUrl(BlokeBotSiteOptions options) =>
        string.IsNullOrWhiteSpace(options.LiveAppUrl) || options.LiveAppUri is not null;

    internal static bool HasCompletePrivacyConfiguration(BlokeBotSiteOptions options) =>
        !string.IsNullOrWhiteSpace(options.ControllerName)
        && PrivacyContactValidation.IsMonitoredAddress(options.PrivacyContact)
        && PrivacyNoticeUrlValidation.IsAbsoluteHttps(options.PrivacyNoticeUrl);
}

internal static class PrivacyContactValidation
{
    internal static bool IsMonitoredAddress(string? contact)
    {
        if (string.IsNullOrWhiteSpace(contact) || contact.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var separator = contact.IndexOf('@', StringComparison.Ordinal);
        return separator > 0
            && separator < contact.Length - 1
            && !contact[(separator + 1)..].Contains('@');
    }
}

internal static class PrivacyNoticeUrlValidation
{
    internal static bool IsAbsoluteHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https";
}
