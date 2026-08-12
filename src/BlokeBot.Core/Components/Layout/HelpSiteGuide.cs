namespace BlokeBot.Core.Components.Layout;

/// <summary>
/// Resolves the optional <c>BlokeBot:HelpSiteBaseUrl</c> deployment value into guide links.
/// The value is optional and never fatal: anything that is not an absolute HTTP or HTTPS address
/// without credentials, query, or fragment resolves to no link at all.
/// </summary>
public static class HelpSiteGuide
{
    /// <summary>
    /// Normalizes a configured base to a trailing slash so a deployment path prefix survives
    /// relative resolution, or returns null when the value cannot be used.
    /// </summary>
    public static Uri? BaseAddress(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
        || !Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
        || uri.Scheme is not ("http" or "https")
        || !string.IsNullOrEmpty(uri.UserInfo)
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment)
            ? null
        : uri.AbsolutePath.EndsWith('/') ? uri
        : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");

    public static Uri? Resolve(string? configured, string? guidePath) =>
        BaseAddress(configured) is { } baseAddress && !string.IsNullOrWhiteSpace(guidePath)
            ? new Uri(baseAddress, guidePath.TrimStart('/'))
            : null;
}
