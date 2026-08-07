namespace BlokeBot.Core;

public sealed record PrivacyNoticeOptions
{
    public string? ControllerName { get; init; }

    public string? PrivacyContact { get; init; }

    public string? NoticeUrl { get; init; }

    public Uri? NoticeUri =>
        Uri.TryCreate(NoticeUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https"
            ? uri
            : null;
}

public static class PrivacyNoticeOptionsValidation
{
    /// <summary>
    /// Complete privacy configuration is enforced only for online deployments: local offline
    /// runs, development, and the Simulation fixture supply explicit local values instead.
    /// </summary>
    public static bool RequiredFor(bool online, string environmentName) =>
        online
        && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(environmentName, "Simulation", StringComparison.OrdinalIgnoreCase);

    public const string RequiredFailure =
        "Online deployments require BlokeBotPrivacy:ControllerName, a monitored "
        + "BlokeBotPrivacy:PrivacyContact email address, and an absolute HTTPS "
        + "BlokeBotPrivacy:NoticeUrl. Supply the deployment's own values; there is no default.";

    public const string NoticeUrlFailure =
        "BlokeBotPrivacy:NoticeUrl must be an absolute HTTPS URL when configured.";

    public static bool IsComplete(PrivacyNoticeOptions options) =>
        !string.IsNullOrWhiteSpace(options.ControllerName)
        && IsMonitoredAddress(options.PrivacyContact)
        && options.NoticeUri is not null;

    public static bool HasValidNoticeUrlWhenConfigured(PrivacyNoticeOptions options) =>
        string.IsNullOrWhiteSpace(options.NoticeUrl) || options.NoticeUri is not null;

    public static bool IsMonitoredAddress(string? contact)
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
