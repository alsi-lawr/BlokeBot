namespace BlokeBot.Core.Auth.OAuth;

internal static class OAuthRequestUri
{
    public static string CreateCallbackUri(HttpRequest request, string callbackPath)
    {
        var path = callbackPath.StartsWith('/') ? callbackPath : $"/{callbackPath}";
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return $"{request.Scheme}://{request.Host}{pathBase}{path}";
    }
}
