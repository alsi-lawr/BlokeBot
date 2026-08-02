namespace BlokeBot.Site;

public sealed record BlokeBotSiteOptions
{
    public string? LiveAppUrl { get; init; }

    internal Uri? LiveAppUri =>
        Uri.TryCreate(LiveAppUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : null;
}

internal static class BlokeBotSiteOptionsValidation
{
    internal const string LiveAppUrlFailure =
        "BlokeBotSite:LiveAppUrl must be an absolute HTTP or HTTPS URL when configured.";

    internal static bool HasValidLiveAppUrl(BlokeBotSiteOptions options) =>
        string.IsNullOrWhiteSpace(options.LiveAppUrl) || options.LiveAppUri is not null;
}
