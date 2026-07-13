namespace BlokeBot.Auth.Web;

public static class LocalReturnUrl
{
    public static string OrFallback(string? returnUrl, string fallbackUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackUrl);

        if (!IsSafe(fallbackUrl))
        {
            throw new ArgumentException(
                "The fallback URL must be a local app path.",
                nameof(fallbackUrl)
            );
        }

        return IsSafe(returnUrl) ? returnUrl! : fallbackUrl;
    }

    public static bool IsSafe(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return false;
        }

        if (returnUrl[0] != '/')
        {
            return false;
        }

        if (returnUrl.Contains("\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (returnUrl.Contains("%5c", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return returnUrl.Length == 1 || returnUrl[1] != '/';
    }
}
