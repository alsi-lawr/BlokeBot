using System.Net.Http.Headers;

namespace BlokeBot.Twitch;

internal static class HelixRequest
{
    internal static HttpRequestMessage Create(
        HttpMethod method,
        string uri,
        HelixRequestContext context
    )
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            context.AccessToken
        );
        request.Headers.Add("Client-Id", context.ClientId);
        return request;
    }
}
