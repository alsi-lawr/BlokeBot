namespace BlokeBot.Core.Auth.Web;

public static class LocalReturnUrl
{
    public static string OrFallback(string? returnUrl, string fallbackUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackUrl);

        return IsSafe(fallbackUrl) switch
        {
            false => throw new ArgumentException(
                "The fallback URL must be a local app path.",
                nameof(fallbackUrl)
            ),
            true when IsSafe(returnUrl) => returnUrl!,
            true => fallbackUrl,
        };
    }

    public static bool IsSafe(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl[0] == '/'
        && !returnUrl.Contains("\\", StringComparison.Ordinal)
        && !returnUrl.Contains("%5c", StringComparison.OrdinalIgnoreCase)
        && (returnUrl.Length == 1 || returnUrl[1] != '/');
}
